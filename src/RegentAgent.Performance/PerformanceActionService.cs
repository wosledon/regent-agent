using RegentAgent.Storage;

namespace RegentAgent.Performance;

public sealed record PerformanceActionRecord(string Id, string TenantId, string EmployeeId, string ActionType,
    string Status, string? ApprovalId, string Reason, DateTimeOffset CreatedAt, string AppealStatus = "None");

public sealed class PerformanceActionService(JsonDocumentStore<PerformanceActionRecord> actions)
{
    public async Task<PerformanceActionRecord> PerformAsync(string tenantId, string employeeId, string actionType,
        string? approvalId, string reason, CancellationToken cancellationToken)
    {
        if (actionType is not ("Coaching" or "Transfer" or "Demotion" or "Freeze" or "Terminate" or "Reinstate"))
            throw new InvalidOperationException("Invalid performance action.");
        if (actionType == "Terminate" && string.IsNullOrWhiteSpace(approvalId))
            throw new InvalidOperationException("Human Boss approval is required for termination.");
        var record = new PerformanceActionRecord(Guid.NewGuid().ToString("N"), tenantId, employeeId, actionType,
            "Completed", approvalId, reason, DateTimeOffset.UtcNow);
        await actions.PutAsync(record.Id, record, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<PerformanceActionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await actions.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<PerformanceActionRecord> AppealAsync(string id, string tenantId, string reason,
        CancellationToken cancellationToken)
    {
        var record = await actions.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Performance action not found.");
        if (record.TenantId != tenantId) throw new UnauthorizedAccessException();
        record = record with { AppealStatus = reason.Length == 0 ? "Invalid" : "Open" };
        await actions.PutAsync(id, record, cancellationToken);
        return record;
    }
}
