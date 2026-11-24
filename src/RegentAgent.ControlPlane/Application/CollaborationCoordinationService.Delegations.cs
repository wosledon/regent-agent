using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class CollaborationCoordinationService
{
    private sealed record DelegationDetails(int Weight, string AcceptanceCriteria,
        IReadOnlyList<string> EvidenceRequirements, IReadOnlyList<string> RequiredCapabilities, string Reason);

    public Task<TaskCoordinationDto> CreateDelegationAsync(AgentIdentityModel identity,
        CreateTaskDelegationRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.delegate", idempotencyKey, async () =>
        {
            if (request.Weight <= 0 || string.IsNullOrWhiteSpace(request.AcceptanceCriteria)
                || request.EvidenceRequirements.Count == 0)
                throw new InvalidOperationException("Delegation weight, acceptance criteria and evidence requirements are required.");
            var task = await LoadOwnedTaskAsync(identity, request.TaskId, false, cancellationToken);
            await EnsureDelegateEligibleAsync(identity.TenantId, request.DelegateEmployeeId, identity.EmployeeId,
                request.RequiredCapabilities, task.Id, cancellationToken);
            if (await db.TaskCoordinations.AnyAsync(x => x.TenantId == identity.TenantId && x.TaskId == task.Id
                && x.Kind == "Delegation" && (x.Status == "Proposed" || x.Status == "Accepted" || x.Status == "InProgress"),
                cancellationToken))
                throw new InvalidOperationException("Task already has an active delegation.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            var record = await CreateAsync(identity.TenantId, task, identity.EmployeeId, request.DelegateEmployeeId,
                "Delegation", "Proposed", "P2", request.AcceptanceCriteria, new
                {
                    request.Weight, request.AcceptanceCriteria, request.EvidenceRequirements,
                    request.RequiredCapabilities, request.Reason
                }, actor.DisplayName, null, task.LeaseToken, cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> DecideDelegationAsync(AgentIdentityModel identity, Guid id,
        CoordinationDecisionRequest decision, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.delegate.decide", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId, true);
            if (record.Kind != "Delegation" || record.Status != "Proposed")
                throw new InvalidOperationException("Only a proposed delegation can be decided.");
            if (!decision.Accepted && string.IsNullOrWhiteSpace(decision.Reason))
                throw new InvalidOperationException("Rejection reason is required.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            if (!decision.Accepted)
            {
                record.Status = "Rejected";
                record.ResponseJson = System.Text.Json.JsonSerializer.Serialize(new { decision.Reason }, JsonOptions);
                record.CompletedAt = record.UpdatedAt = clock.GetUtcNow();
                record.Version++;
                await AppendAuditAsync(record, "rejected", identity.EmployeeId, actor.DisplayName, new { decision.Reason }, cancellationToken);
                await SaveAsync(cancellationToken);
                return ToDto(record);
            }

            var details = System.Text.Json.JsonSerializer.Deserialize<DelegationDetails>(record.DetailsJson, JsonOptions)
                ?? throw new InvalidOperationException("Delegation details are invalid.");
            await EnsureDelegateEligibleAsync(identity.TenantId, identity.EmployeeId, record.RequesterEmployeeId,
                details.RequiredCapabilities, record.TaskId, cancellationToken);
            var task = await LoadTaskAsync(identity.TenantId, record.TaskId, cancellationToken);
            if (await ResolveOwnerAsync(task, cancellationToken) != record.RequesterEmployeeId
                || !ActiveTaskStatuses.Contains(task.Status))
                throw new InvalidOperationException("Delegation source task is no longer available.");
            task.Assignee = actor.DisplayName;
            task.AssigneeEmployeeId = actor.Id;
            task.AccountableEmployeeId = record.RequesterEmployeeId;
            task.LeaseOwner = null;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.Status = "Assigned";
            Touch(task);
            record.Status = "Accepted";
            record.ResponseJson = System.Text.Json.JsonSerializer.Serialize(new { decision.Reason }, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            await AppendAuditAsync(record, "accepted", identity.EmployeeId, actor.DisplayName, new { decision.Reason }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> CancelDelegationAsync(AgentIdentityModel identity, Guid id,
        string reason, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.delegate.cancel", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId);
            if (record.Kind != "Delegation" || record.Status != "Proposed" || record.RequesterEmployeeId != identity.EmployeeId)
                throw new InvalidOperationException("Only the requester can cancel a proposed delegation.");
            if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Cancellation reason is required.");
            record.Status = "Cancelled";
            record.ResponseJson = System.Text.Json.JsonSerializer.Serialize(new { reason }, JsonOptions);
            record.CompletedAt = record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            await AppendAuditAsync(record, "cancelled", identity.EmployeeId, actor.DisplayName, new { reason }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);
}
