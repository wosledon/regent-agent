using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed record MemoryClaim(string Id, string TenantId, string Scope, string Content, string SourceRef, double Confidence, string Taint, string Status, int Version, string? SupersedesId = null);
public sealed record CorrectionTask(string Id, string TenantId, string ClaimId, string Reason, string Status, DateTimeOffset CreatedAt);

public sealed class MemoryService(JsonDocumentStore<MemoryClaim> claims, JsonDocumentStore<CorrectionTask> corrections)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await claims.GetAllAsync(cancellationToken)).Count > 0) return;
        await claims.PutAsync("memory-1", new MemoryClaim("memory-1", "11111111-1111-1111-1111-111111111111", "organization",
            "Provider contract tests must cover native streaming errors.", "decision:provider-contract", 0.92, "trusted", "Active", 1), cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryClaim>> SearchAsync(string tenantId, string? query, CancellationToken cancellationToken) =>
        (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && x.Status == "Active" &&
            (string.IsNullOrWhiteSpace(query) || x.Content.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();

    public async Task<MemoryClaim> CorrectAsync(string claimId, string tenantId, string content, string reason, CancellationToken cancellationToken)
    {
        var current = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (current.TenantId != tenantId) throw new UnauthorizedAccessException();
        var corrected = new MemoryClaim(Guid.NewGuid().ToString("N"), tenantId, current.Scope, content,
            current.SourceRef, current.Confidence, current.Taint, "Active", current.Version + 1, current.Id);
        await claims.PutAsync(current.Id, current with { Status = "Superseded" }, cancellationToken);
        await claims.PutAsync(corrected.Id, corrected, cancellationToken);
        await corrections.PutAsync(Guid.NewGuid().ToString("N"), new CorrectionTask(Guid.NewGuid().ToString("N"), tenantId, claimId, reason, "Completed", DateTimeOffset.UtcNow), cancellationToken);
        return corrected;
    }
}
