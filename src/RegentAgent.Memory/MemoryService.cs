using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed record MemoryClaim(string Id, string TenantId, string Scope, string Content, string SourceRef, double Confidence, string Taint, string Status, int Version, string? SupersedesId = null,
    DateTimeOffset? LastAccessedAt = null, int RecallCount = 0);
public sealed record CorrectionTask(string Id, string TenantId, string ClaimId, string Reason, string Status, DateTimeOffset CreatedAt);

public sealed class MemoryService(JsonDocumentStore<MemoryClaim> claims, JsonDocumentStore<CorrectionTask> corrections)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await claims.GetAllAsync(cancellationToken)).Count > 0) return;
        await claims.PutAsync("memory-1", new MemoryClaim("memory-1", "11111111-1111-1111-1111-111111111111", "organization",
            "Provider contract tests must cover native streaming errors.", "decision:provider-contract", 0.92, "trusted", "Active", 1), cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryClaim>> SearchAsync(string tenantId, string? query, CancellationToken cancellationToken)
    {
        var matches = (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && x.Status == "Active" &&
            (string.IsNullOrWhiteSpace(query) || x.Content.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var claim in matches)
            await claims.PutAsync(claim.Id, claim with { LastAccessedAt = DateTimeOffset.UtcNow, RecallCount = claim.RecallCount + 1 }, cancellationToken);
        return matches;
    }

    public async Task DecayAsync(string tenantId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var claim in (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && x.Status == "Active"))
        {
            var days = Math.Max(0, (now - (claim.LastAccessedAt ?? now)).TotalDays);
            var confidence = Math.Max(0.1, claim.Confidence - days * 0.01);
            await claims.PutAsync(claim.Id, claim with { Confidence = confidence }, cancellationToken);
        }
    }

    public async Task ForgetAsync(string claimId, string tenantId, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        await claims.PutAsync(claimId, claim with { Status = "Forgotten" }, cancellationToken);
    }

    public async Task DeleteAsync(string claimId, string tenantId, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        var related = (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId &&
            (x.Id == claimId || x.SupersedesId == claimId)).Select(x => x.Id).ToArray();
        foreach (var id in related) await claims.DeleteAsync(id, cancellationToken);
        foreach (var correction in (await corrections.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && related.Contains(x.ClaimId)).ToArray())
            await corrections.DeleteAsync(correction.Id, cancellationToken);
    }

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
