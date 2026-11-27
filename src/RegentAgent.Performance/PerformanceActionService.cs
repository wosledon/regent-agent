using RegentAgent.Storage;

namespace RegentAgent.Performance;

public sealed record PerformanceActionRecord(string Id, string TenantId, string EmployeeId, string ActionType,
    string Status, string? ApprovalId, string Reason, DateTimeOffset CreatedAt, string AppealStatus = "None",
    string? AppealReason = null, string? AppealResolution = null, string? AppealReviewer = null);

public sealed class PerformanceActionService(JsonDocumentStore<PerformanceActionRecord> actions,
    TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<PerformanceActionRecord> PerformAsync(string tenantId, string employeeId, string actionType,
        string? approvalId, string reason, CancellationToken cancellationToken)
    {
        if (string.Equals(actionType, "Terminate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Termination must execute through ApprovalExecutionService.");
        if (actionType is not ("Coaching" or "Transfer" or "Demotion" or "Freeze" or "Reinstate"))
            throw new InvalidOperationException("Invalid performance action.");
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Employee and reason are required.");
        var record = new PerformanceActionRecord(Guid.NewGuid().ToString("N"), tenantId, employeeId, actionType,
            "Completed", approvalId, reason, clock.GetUtcNow());
        await actions.PutAsync(record.Id, record, cancellationToken);
        return record;
    }

    public PerformanceActionRecord CreateApprovedTerminationRecord(string tenantId, string employeeId,
        string approvalId, string reason, DateTimeOffset? createdAt = null)
    {
        ValidateTermination(tenantId, employeeId, approvalId, reason);
        return new PerformanceActionRecord(TerminationRecordId(approvalId), tenantId, employeeId, "Terminate",
            "Completed", approvalId, reason, createdAt ?? clock.GetUtcNow());
    }

    public async Task<PerformanceActionRecord> RecordApprovedTerminationAsync(string tenantId, string employeeId,
        string approvalId, string reason, CancellationToken cancellationToken, DateTimeOffset? createdAt = null)
    {
        var id = TerminationRecordId(approvalId);
        var existing = await actions.GetAsync(id, cancellationToken);
        if (existing is not null)
        {
            if (existing.TenantId != tenantId || existing.EmployeeId != employeeId
                || existing.ActionType != "Terminate" || existing.ApprovalId != approvalId)
                throw new InvalidOperationException("Termination projection conflicts with the approved action.");
            return existing;
        }
        var record = CreateApprovedTerminationRecord(tenantId, employeeId, approvalId, reason, createdAt);
        await actions.PutAsync(id, record, cancellationToken);
        return record;
    }

    public async Task<PerformanceActionRecord?> GetAsync(string id, string tenantId,
        CancellationToken cancellationToken)
    {
        var record = await actions.GetAsync(id, cancellationToken);
        return record is not null && record.TenantId == tenantId ? record : null;
    }

    public async Task<IReadOnlyList<PerformanceActionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await actions.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<PerformanceActionRecord> AppealAsync(string id, string tenantId, string reason,
        CancellationToken cancellationToken)
    {
        var record = await actions.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Performance action not found.");
        if (record.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Appeal reason is required.");
        if (record.AppealStatus is not ("None" or "Rejected")) throw new InvalidOperationException("Appeal is not open for submission.");
        record = record with { AppealStatus = "Open", AppealReason = reason, AppealResolution = null, AppealReviewer = null };
        await actions.PutAsync(id, record, cancellationToken);
        return record;
    }

    public async Task<PerformanceActionRecord> ResolveAppealAsync(string id, string tenantId, string reviewer,
        string resolution, bool upheld, CancellationToken cancellationToken)
    {
        var record = await actions.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Performance action not found.");
        if (record.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (record.AppealStatus != "Open") throw new InvalidOperationException("Performance action has no open appeal.");
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(resolution))
            throw new InvalidOperationException("Reviewer and resolution are required.");
        record = record with
        {
            AppealStatus = upheld ? "Upheld" : "Overturned", AppealReviewer = reviewer,
            AppealResolution = resolution, Status = upheld ? record.Status : "Reversed"
        };
        await actions.PutAsync(id, record, cancellationToken);
        return record;
    }

    private static string TerminationRecordId(string approvalId)
    {
        if (!Guid.TryParse(approvalId, out var parsed) || parsed == Guid.Empty)
            throw new InvalidOperationException("A valid approval ID is required.");
        return $"termination-{parsed:N}";
    }

    private static void ValidateTermination(string tenantId, string employeeId, string approvalId, string reason)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(employeeId)
            || string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Tenant, employee and reason are required.");
        _ = TerminationRecordId(approvalId);
    }
}
