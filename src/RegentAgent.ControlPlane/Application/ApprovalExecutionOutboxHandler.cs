using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Realtime;
using RegentAgent.Memory;
using RegentAgent.Performance;

namespace RegentAgent.ControlPlane.Application;

public sealed class ApprovalExecutionOutboxHandler(
    RegentDbContext db,
    TenantHubConnectionRegistry connections,
    IPersonalMemoryIsolation memory,
    PerformanceActionService performance) : IOutboxEventHandler
{
    public string EventType => ApprovalExecutionEvent.EventType;

    public async Task HandleAsync(OutboxRecord message, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(message.PayloadJson);
        var payload = document.RootElement;
        var approvalId = ReadGuid(payload, "approval_id");
        if (message.Id != ApprovalExecutionEvent.DeterministicId(approvalId))
            throw new InvalidOperationException("Approval execution outbox ID is not deterministic for its approval.");
        if (message.EventType != EventType)
            throw new InvalidOperationException("Approval execution outbox event type is invalid.");

        var tenantId = ReadGuid(payload, "tenant_id");
        if (tenantId != message.TenantId)
            throw new InvalidOperationException("Outbox tenant does not match its payload.");
        var actorId = ReadGuid(payload, "actor_id");
        var actionType = ReadString(payload, "action_type");
        if (actionType is not (ApprovalActionTypes.EmployeeOffboard or ApprovalActionTypes.EmployeeTerminate
                or ApprovalActionTypes.HiringHire or ApprovalActionTypes.HiringRehire))
            throw new InvalidOperationException("Outbox approval action is invalid.");

        var targetEmployeeId = ReadOptionalGuid(payload, "target_employee_id");
        var targetCandidateId = ReadOptionalGuid(payload, "target_candidate_id");
        var targetPositionId = ReadOptionalGuid(payload, "target_position_id");
        var employeeId = ReadOptionalGuid(payload, "employee_id");
        var employmentPeriodId = ReadOptionalGuid(payload, "employment_period_id");
        var occurredAt = ReadDateTimeOffset(payload, "occurred_at");
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved execution approval no longer exists.");
        var binding = new ApprovalBinding(tenantId, actionType, targetEmployeeId, targetCandidateId, targetPositionId);
        if (approval.State != ApprovalState.Approved
            || approval.ActionType != binding.NormalizedActionType
            || approval.TargetEmployeeId != targetEmployeeId
            || approval.TargetCandidateId != targetCandidateId
            || approval.TargetPositionId != targetPositionId
            || !binding.MatchesV2Hash(approval.ActionHash)
            || approval.ConsumedAt is not { } consumedAt
            || approval.ConsumedBy is not { } consumedBy
            || consumedBy != actorId
            || Math.Abs((consumedAt - occurredAt).TotalSeconds) > 1
            || approval.ExpiresAt <= consumedAt
            || approval.DecidedAt is not { } decidedAt
            || decidedAt > consumedAt
            || !ApprovalExecutionRules.HasValidDecider(approval))
            throw new InvalidOperationException("Approval execution message is not backed by a valid consumed approval.");

        if (actionType is ApprovalActionTypes.EmployeeOffboard or ApprovalActionTypes.EmployeeTerminate)
        {
            if (targetEmployeeId is not { } terminatedEmployeeId || employeeId != terminatedEmployeeId
                || employmentPeriodId is not { })
                throw new InvalidOperationException("Termination outbox target or employment period is invalid.");
            var period = await db.EmploymentPeriods.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == employmentPeriodId
                    && x.EmployeeId == terminatedEmployeeId && x.EndedAt != null, cancellationToken)
                ?? throw new KeyNotFoundException("Terminated employment period no longer exists.");
            var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == terminatedEmployeeId, cancellationToken)
                ?? throw new KeyNotFoundException("Approved employee no longer exists.");
            await connections.DisconnectEmployeeAsync(tenantId, terminatedEmployeeId, period.Id, cancellationToken);
            var displayNameCount = await db.Employees.CountAsync(
                x => x.TenantId == tenantId && x.DisplayName == employee.DisplayName, cancellationToken);
            await memory.IsolatePersonalClaimsAsync(tenantId.ToString(), employee.DisplayName, cancellationToken,
                employee.Id.ToString(), allowLegacyNameFallback: displayNameCount == 1,
                authorEmploymentPeriodId: period.Id.ToString(), createdBefore: period.EndedAt);

            if (actionType == ApprovalActionTypes.EmployeeTerminate)
            {
                var reason = ReadString(payload, "reason");
                await performance.RecordApprovedTerminationAsync(tenantId.ToString(), terminatedEmployeeId.ToString(),
                    approvalId.ToString(), reason, cancellationToken, occurredAt);
            }
            return;
        }

        if (employeeId is null || !await db.Employees.AnyAsync(
                x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken))
            throw new InvalidOperationException("Approval execution employee is invalid.");
        if (actionType == ApprovalActionTypes.HiringRehire && targetEmployeeId != employeeId)
            throw new InvalidOperationException("Rehire outbox employee does not match its approved target.");
    }

    private static Guid ReadGuid(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || !Guid.TryParse(value.GetString(), out var parsed)
            || parsed == Guid.Empty)
            throw new InvalidOperationException($"Outbox payload {name} is invalid.");
        return parsed;
    }

    private static Guid? ReadOptionalGuid(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return Guid.TryParse(value.GetString(), out var parsed) && parsed != Guid.Empty
            ? parsed : throw new InvalidOperationException($"Outbox payload {name} is invalid.");
    }

    private static string ReadString(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"Outbox payload {name} is invalid.");

    private static DateTimeOffset ReadDateTimeOffset(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value)
            || !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed))
            throw new InvalidOperationException($"Outbox payload {name} is invalid.");
        return parsed;
    }
}
