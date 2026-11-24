using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class CollaborationCoordinationService
{
    private static readonly TimeSpan InterruptDeadline = TimeSpan.FromHours(4);

    public Task<TaskCoordinationDto> CreateInterruptAsync(AgentIdentityModel identity,
        CreateInterruptRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.interrupt", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            if (request.Priority is not ("P0" or "P1") || string.IsNullOrWhiteSpace(request.Reason))
                throw new InvalidOperationException("Only P0/P1 interrupts with a reason are allowed.");
            var task = await LoadTaskAsync(identity.TenantId, request.TaskId, cancellationToken);
            var targetId = await ResolveOwnerAsync(task, cancellationToken);
            if (targetId == Guid.Empty || targetId == identity.EmployeeId || !ActiveTaskStatuses.Contains(task.Status))
                throw new InvalidOperationException("Interrupt target task is not active or belongs to the requester.");
            await LoadTargetAsync(identity.TenantId, targetId, cancellationToken);
            if (await db.TaskCoordinations.AnyAsync(x => x.TenantId == identity.TenantId && x.TaskId == task.Id
                && x.Kind == "Interrupt" && (x.Status == "Requested" || x.Status == "Suspended"
                || x.Status == "ReadyToResume"), cancellationToken))
                throw new InvalidOperationException("Task already has an active interrupt.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            var record = await CreateAsync(identity.TenantId, task, identity.EmployeeId, targetId,
                "Interrupt", "Requested", request.Priority, request.Reason, new { request.Reason },
                actor.DisplayName, clock.GetUtcNow() + InterruptDeadline, task.LeaseToken, cancellationToken);
            var recentInterrupts = await db.TaskCoordinations.CountAsync(x => x.TenantId == identity.TenantId
                && x.TargetEmployeeId == targetId && x.Kind == "Interrupt"
                && x.CreatedAt >= clock.GetUtcNow().AddHours(-1), cancellationToken);
            if (recentInterrupts >= options.MaxInterruptsPerHour)
                await audit.AppendAsync(identity.TenantId, "collaboration.attention_overload", actor.DisplayName,
                    new { employee_id = targetId, interrupt_count = recentInterrupts, window_minutes = 60 }, cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> AcknowledgeInterruptAsync(AgentIdentityModel identity, Guid id,
        InterruptCheckpointRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.interrupt.acknowledge", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId, true);
            if (record.Kind != "Interrupt" || record.Status != "Requested" || identity.TaskId != record.TaskId)
                throw new InvalidOperationException("Only the target Agent can acknowledge this interrupt for its task.");
            if (record.Deadline <= clock.GetUtcNow())
                throw new InvalidOperationException("Interrupt deadline has passed.");
            var ownedTask = await LoadOwnedTaskAsync(identity, record.TaskId, true, cancellationToken);
            if (ownedTask.LeaseToken != request.LeaseToken
                || (record.LeaseToken is not null && record.LeaseToken != request.LeaseToken))
                throw new UnauthorizedAccessException("Interrupt lease token does not match.");
            ValidatePayload(request.CheckpointJson);
            var checkpointHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CheckpointJson))).ToLowerInvariant();
            ownedTask.CheckpointJson = request.CheckpointJson;
            ownedTask.Status = "Interrupted";
            ownedTask.LeaseOwner = null;
            ownedTask.LeaseToken = null;
            ownedTask.LeaseExpiresAt = null;
            Touch(ownedTask);
            record.LeaseToken = request.LeaseToken;
            record.Status = "Suspended";
            record.ResponseJson = JsonSerializer.Serialize(new { checkpointHash }, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            await AppendAuditAsync(record, "acknowledged", identity.EmployeeId, actor.DisplayName, new { checkpointHash }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> ResolveInterruptAsync(AgentIdentityModel identity, Guid id,
        CoordinationResolutionRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.interrupt.resolve", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            if (string.IsNullOrWhiteSpace(request.Resolution)) throw new InvalidOperationException("Resolution is required.");
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId);
            if (record.Kind != "Interrupt" || record.RequesterEmployeeId != identity.EmployeeId || record.Status != "Suspended")
                throw new InvalidOperationException("Only the requester can resolve a suspended interrupt.");
            record.Status = "ReadyToResume";
            record.ResponseJson = JsonSerializer.Serialize(request, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            await AppendAuditAsync(record, "resolved", identity.EmployeeId, actor.DisplayName, new { request.Resolution }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<InterruptResumeResult> ResumeInterruptAsync(AgentIdentityModel identity, Guid id,
        InterruptResumeRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.interrupt.resume", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId, true);
            if (record.Kind != "Interrupt" || record.Status != "ReadyToResume" || identity.TaskId != record.TaskId)
                throw new InvalidOperationException("Interrupt is not ready for this target Agent to resume.");
            if (record.LeaseToken != request.LeaseToken) throw new UnauthorizedAccessException("Interrupt lease token does not match.");
            var task = await LoadTaskAsync(identity.TenantId, record.TaskId, cancellationToken);
            if (task.Status != "Interrupted" || await ResolveOwnerAsync(task, cancellationToken) != identity.EmployeeId)
                throw new InvalidOperationException("Interrupted task is no longer owned by the target Agent.");
            var leaseToken = Guid.NewGuid().ToString("N");
            var leaseExpiresAt = clock.GetUtcNow().AddMinutes(2);
            task.Status = "InProgress";
            task.LeaseOwner = identity.RunnerId ?? "agent-resume";
            task.LeaseToken = leaseToken;
            task.LeaseExpiresAt = leaseExpiresAt;
            Touch(task);
            record.Status = "Resumed";
            record.CompletedAt = record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            await AppendAuditAsync(record, "resumed", identity.EmployeeId, actor.DisplayName, new { }, cancellationToken);
            await SaveAsync(cancellationToken);
            return new InterruptResumeResult(ToDto(record), leaseToken, leaseExpiresAt);
        }, cancellationToken);

    public Task<TaskCoordinationDto> CancelInterruptAsync(AgentIdentityModel identity, Guid id,
        CoordinationCancelRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.interrupt.cancel", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId);
            if (record.Kind != "Interrupt" || record.RequesterEmployeeId != identity.EmployeeId || record.Status != "Requested")
                throw new InvalidOperationException("Only the requester can cancel a pending interrupt.");
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Cancellation reason is required.");
            record.Status = "Cancelled";
            record.ResponseJson = JsonSerializer.Serialize(request, JsonOptions);
            record.CompletedAt = record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            await AppendAuditAsync(record, "cancelled", identity.EmployeeId, actor.DisplayName, new { request.Reason }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);
}
