using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class CollaborationCoordinationService
{
    public Task<TaskCoordinationDto> CreateHelpAsync(AgentIdentityModel identity,
        CreateHelpRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.help", idempotencyKey, async () =>
        {
            var task = await LoadOwnedTaskAsync(identity, request.TaskId, false, cancellationToken);
            if (string.IsNullOrWhiteSpace(request.Question) || request.AttemptedSteps.Count == 0
                || string.IsNullOrWhiteSpace(request.ExpectedAnswer))
                throw new InvalidOperationException("Help question, attempted steps and expected answer are required.");
            if (request.Deadline <= clock.GetUtcNow() || request.Deadline > clock.GetUtcNow().AddDays(30))
                throw new InvalidOperationException("Help deadline must be in the future and within 30 days.");
            await LoadTargetAsync(identity.TenantId, request.HelperEmployeeId, cancellationToken);
            if (request.HelperEmployeeId == identity.EmployeeId)
                throw new InvalidOperationException("Help target must differ from the requester.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            var record = await CreateAsync(identity.TenantId, task, identity.EmployeeId, request.HelperEmployeeId,
                "Help", "Open", "P2", request.Question, request, actor.DisplayName, request.Deadline,
                task.LeaseToken, cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> RespondHelpAsync(AgentIdentityModel identity, Guid id,
        HelpResponseRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.help.respond", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId, true);
            if (record.Kind != "Help" || record.Status != "Open")
                throw new InvalidOperationException("Only an open help request can be answered.");
            if (request.ResponseType is not ("Answer" or "Decision" or "Redirect" or "NeedMoreContext" or "Reject"))
                throw new InvalidOperationException("Help response type is invalid.");
            if (request.ResponseType != "NeedMoreContext" && string.IsNullOrWhiteSpace(request.Answer))
                throw new InvalidOperationException("Help response content is required.");
            if (clock.GetUtcNow() > record.Deadline)
                throw new InvalidOperationException("Help deadline has passed; escalation is required.");
            if (request.ResponseType == "Redirect")
            {
                if (request.RedirectEmployeeId is not { } redirectId)
                    throw new InvalidOperationException("Redirect target is required.");
                if (redirectId == record.RequesterEmployeeId)
                    throw new InvalidOperationException("Help cannot be redirected to the requester.");
                await LoadTargetAsync(identity.TenantId, redirectId, cancellationToken);
                record.TargetEmployeeId = redirectId;
            }
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            record.Status = request.ResponseType switch
            {
                "Answer" or "Decision" => "Answered",
                "Redirect" => "Open",
                "NeedMoreContext" => "NeedMoreContext",
                _ => "Rejected"
            };
            record.ResponseJson = JsonSerializer.Serialize(request, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            if (record.Status == "Rejected") record.CompletedAt = record.UpdatedAt;
            record.Version++;
            await AppendAuditAsync(record, "responded", identity.EmployeeId, actor.DisplayName, new { request.ResponseType }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> AddHelpContextAsync(AgentIdentityModel identity, Guid id,
        HelpContextRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.help.context", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId);
            if (record.Kind != "Help" || record.RequesterEmployeeId != identity.EmployeeId
                || record.Status != "NeedMoreContext")
                throw new InvalidOperationException("Only the requester can add context to a help request awaiting context.");
            if (string.IsNullOrWhiteSpace(request.Context)) throw new InvalidOperationException("Additional context is required.");
            if (record.Deadline <= clock.GetUtcNow()) throw new InvalidOperationException("Help deadline has passed.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            record.Status = "Open";
            record.ResponseJson = JsonSerializer.Serialize(new { additionalContext = request }, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            await AppendAuditAsync(record, "context_added", identity.EmployeeId, actor.DisplayName,
                new { evidence_count = request.EvidenceRefs.Count }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> ResolveHelpAsync(AgentIdentityModel identity, Guid id,
        string resolution, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.help.resolve", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId);
            if (record.Kind != "Help" || record.RequesterEmployeeId != identity.EmployeeId
                || record.Status != "Answered")
                throw new InvalidOperationException("Only the requester can resolve an answered help request.");
            if (string.IsNullOrWhiteSpace(resolution)) throw new InvalidOperationException("Resolution is required.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            record.Status = "Resolved";
            record.ResponseJson = JsonSerializer.Serialize(new { resolution }, JsonOptions);
            record.CompletedAt = record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            await AppendAuditAsync(record, "resolved", identity.EmployeeId, actor.DisplayName, new { resolution }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);
}
