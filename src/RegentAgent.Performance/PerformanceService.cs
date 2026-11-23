using RegentAgent.Storage;

namespace RegentAgent.Performance;

public sealed record KpiDefinition(string Id, string TenantId, string Name, string Formula, string Target);
public sealed record KpiSnapshot(string Id, string TenantId, string SubjectType, string SubjectId, string MetricKey, decimal Value, string Target, string Status, DateTimeOffset CalculatedAt);
public sealed record PerformanceReview(string Id, string TenantId, string EmployeeId, string Period, string Conclusion, string EvidenceJson, string Status, DateTimeOffset CreatedAt);

public sealed class PerformanceService(
    JsonDocumentStore<KpiSnapshot> snapshots,
    JsonDocumentStore<PerformanceReview> reviews)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await snapshots.GetAllAsync(cancellationToken)).Count > 0) return;
        await snapshots.PutAsync("kpi-1", new KpiSnapshot("kpi-1", "11111111-1111-1111-1111-111111111111", "company", "11111111-1111-1111-1111-111111111111",
            "task_success_rate", 0.9m, ">=0.9", "OnTrack", DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<IReadOnlyList<KpiSnapshot>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await snapshots.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<KpiSnapshot> RecordAsync(KpiSnapshot snapshot, CancellationToken cancellationToken)
    {
        await snapshots.PutAsync(snapshot.Id, snapshot, cancellationToken);
        return snapshot;
    }

    public async Task<PerformanceReview> ReviewAsync(string employeeId, string tenantId, string conclusion, string evidenceJson, CancellationToken cancellationToken)
    {
        var review = new PerformanceReview(Guid.NewGuid().ToString("N"), tenantId, employeeId,
            DateTimeOffset.UtcNow.ToString("yyyy-MM"), conclusion, evidenceJson, "Completed", DateTimeOffset.UtcNow);
        await reviews.PutAsync(review.Id, review, cancellationToken);
        return review;
    }
}
