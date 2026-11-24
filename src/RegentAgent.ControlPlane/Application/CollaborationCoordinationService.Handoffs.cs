using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class CollaborationCoordinationService
{
    public Task<TaskCoordinationDto> CreateHandoffAsync(AgentIdentityModel identity,
        CreateHandoffRequest request, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.handoff", idempotencyKey, async () =>
        {
            var task = await LoadOwnedTaskAsync(identity, request.TaskId, true, cancellationToken);
            if (task.LeaseToken != request.LeaseToken) throw new UnauthorizedAccessException("Lease token does not match.");
            ValidateHandoffPackage(request.Package);
            if (!string.Equals(task.CheckpointJson, request.Package.CheckpointJson, StringComparison.Ordinal))
                throw new InvalidOperationException("Handoff checkpoint does not match the task safe point.");
            await EnsureDelegateEligibleAsync(identity.TenantId, request.RecipientEmployeeId, identity.EmployeeId,
                [], task.Id, cancellationToken);
            await ValidateArtifactRefsAsync(task, request.Package.ArtifactRefs, cancellationToken);
            if (await db.TaskCoordinations.AnyAsync(x => x.TenantId == identity.TenantId && x.TaskId == task.Id
                && x.Kind == "Handoff" && x.Status == "Requested", cancellationToken))
                throw new InvalidOperationException("Task already has a pending handoff.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            var record = await CreateAsync(identity.TenantId, task, identity.EmployeeId, request.RecipientEmployeeId,
                "Handoff", "Requested", "P1", request.Package.NextAction, request.Package, actor.DisplayName,
                null, task.LeaseToken, cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    public Task<TaskCoordinationDto> DecideHandoffAsync(AgentIdentityModel identity, Guid id,
        CoordinationDecisionRequest decision, string? idempotencyKey, CancellationToken cancellationToken) =>
        WriteAsync(identity.TenantId, identity.EmployeeId, "collaboration.handoff.decide", idempotencyKey, async () =>
        {
            RequireTaskScope(identity);
            var record = await LoadCoordinationAsync(identity.TenantId, id, cancellationToken);
            RequireParticipant(record, identity.EmployeeId, true);
            if (record.Kind != "Handoff" || record.Status != "Requested")
                throw new InvalidOperationException("Only a requested handoff can be decided.");
            if (!decision.Accepted && string.IsNullOrWhiteSpace(decision.Reason))
                throw new InvalidOperationException("Rejection reason is required.");
            var actor = await db.Employees.AsNoTracking().SingleAsync(
                x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
            record.ResponseJson = JsonSerializer.Serialize(new { decision.Accepted, decision.Reason }, JsonOptions);
            record.UpdatedAt = clock.GetUtcNow();
            record.Version++;
            if (!decision.Accepted)
            {
                record.Status = "Rejected";
                record.CompletedAt = record.UpdatedAt;
                await AppendAuditAsync(record, "rejected", identity.EmployeeId, actor.DisplayName, new { decision.Reason }, cancellationToken);
                await SaveAsync(cancellationToken);
                return ToDto(record);
            }

            var package = JsonSerializer.Deserialize<HandoffPackageDto>(record.DetailsJson, JsonOptions)
                ?? throw new InvalidOperationException("Handoff package is invalid.");
            ValidateHandoffPackage(package);
            await EnsureDelegateEligibleAsync(identity.TenantId, identity.EmployeeId, record.RequesterEmployeeId,
                [], record.TaskId, cancellationToken);
            await ValidateArtifactRefsAsync(await LoadTaskAsync(identity.TenantId, record.TaskId, cancellationToken),
                package.ArtifactRefs, cancellationToken);
            var task = await LoadTaskAsync(identity.TenantId, record.TaskId, cancellationToken);
            if (await ResolveOwnerAsync(task, cancellationToken) != record.RequesterEmployeeId
                || !ActiveTaskStatuses.Contains(task.Status))
                throw new InvalidOperationException("Handoff source task is no longer available.");
            if (task.LeaseToken != record.LeaseToken || task.LeaseExpiresAt <= clock.GetUtcNow())
                throw new InvalidOperationException("Handoff lease is no longer valid.");
            if (!string.Equals(task.CheckpointJson, package.CheckpointJson, StringComparison.Ordinal))
                throw new InvalidOperationException("Task checkpoint changed after the handoff request.");
            task.Assignee = actor.DisplayName;
            task.AssigneeEmployeeId = actor.Id;
            task.AccountableEmployeeId = actor.Id;
            task.LeaseOwner = null;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.Status = "Assigned";
            Touch(task);
            record.Status = "Accepted";
            record.CompletedAt = record.UpdatedAt;
            await AppendAuditAsync(record, "accepted", identity.EmployeeId, actor.DisplayName, new { decision.Reason }, cancellationToken);
            await SaveAsync(cancellationToken);
            return ToDto(record);
        }, cancellationToken);

    private void ValidateHandoffPackage(HandoffPackageDto package)
    {
        if (string.IsNullOrWhiteSpace(package.Goal) || string.IsNullOrWhiteSpace(package.AcceptanceCriteria)
            || string.IsNullOrWhiteSpace(package.NextAction))
            throw new InvalidOperationException("Handoff goal, acceptance criteria and next action are required.");
        ValidatePayload(package.CheckpointJson);
    }

    private async Task ValidateArtifactRefsAsync(TaskRecord task, IReadOnlyList<string> artifactRefs,
        CancellationToken cancellationToken)
    {
        foreach (var reference in artifactRefs)
        {
            if (!reference.StartsWith("artifact:", StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParse(reference["artifact:".Length..], out var artifactId)
                || !await db.Artifacts.AnyAsync(x => x.TenantId == task.TenantId && x.Id == artifactId
                    && x.TaskId == task.Id && x.UploadState == "Ready", cancellationToken))
                throw new UnauthorizedAccessException("Handoff contains an artifact outside the authorized task scope.");
        }
    }
}
