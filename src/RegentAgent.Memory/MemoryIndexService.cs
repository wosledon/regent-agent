using RegentAgent.Contracts.Memory;
using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed class MemoryIndexService(
    JsonDocumentStore<MemoryClaim> claims,
    JsonDocumentStore<MemoryVectorRecord> vectors,
    JsonDocumentStore<MemoryFinding> findings,
    MemoryGraphService graph,
    TimeProvider clock)
{
    private const double ConflictThreshold = 0.92;
    private const double MinimumRecallScore = 0.30;

    public async Task IndexClaimAsync(MemoryClaim claim, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(claim.Content)) throw new InvalidOperationException("Memory content is required.");
        var hash = LocalEmbeddingProvider.ContentHash(claim.Content);
        var existing = await vectors.GetAsync(claim.Id, cancellationToken);
        if (existing is null)
        {
            await vectors.PutAsync(claim.Id, new MemoryVectorRecord(
                claim.Id, claim.TenantId, hash, LocalEmbeddingProvider.ModelVersion,
                LocalEmbeddingProvider.Embed(claim.Content), clock.GetUtcNow()), cancellationToken);
        }
        else
        {
            if (existing.TenantId != claim.TenantId) throw new UnauthorizedAccessException();
            if (existing.ContentHash != hash || existing.ModelVersion != LocalEmbeddingProvider.ModelVersion)
                await vectors.PutAsync(claim.Id, existing with
                {
                    ContentHash = hash, ModelVersion = LocalEmbeddingProvider.ModelVersion,
                    Vector = LocalEmbeddingProvider.Embed(claim.Content), IndexedAt = clock.GetUtcNow()
                }, cancellationToken);
        }
        await graph.EnsureClaimNodeAsync(claim.TenantId, claim.Id, claim.Content[..Math.Min(claim.Content.Length, 80)], cancellationToken);
    }

    public async Task RemoveFromRecallAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        var vector = await vectors.GetAsync(claimId, cancellationToken);
        if (vector is not null)
        {
            if (vector.TenantId != tenantId) throw new UnauthorizedAccessException();
            await vectors.DeleteAsync(claimId, cancellationToken);
        }
    }

    public async Task DeleteClaimAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        await RemoveFromRecallAsync(tenantId, claimId, cancellationToken);
        await graph.DeleteClaimAsync(tenantId, claimId, cancellationToken);
    }

    public async Task ReplaceClaimAsync(string tenantId, string oldClaimId, MemoryClaim corrected,
        CancellationToken cancellationToken)
    {
        if (corrected.TenantId != tenantId) throw new UnauthorizedAccessException();
        var edges = await graph.GetClaimEdgesAsync(tenantId, oldClaimId, cancellationToken);
        await DeleteClaimAsync(tenantId, oldClaimId, cancellationToken);
        await IndexClaimAsync(corrected, cancellationToken);
        foreach (var edge in edges)
            await graph.AddEdgeAsync(new MemoryGraphEdge(Guid.NewGuid().ToString("N"), tenantId,
                edge.FromClaimId == oldClaimId ? corrected.Id : edge.FromClaimId,
                edge.ToClaimId == oldClaimId ? corrected.Id : edge.ToClaimId, edge.Relation), cancellationToken);
    }

    public async Task<int> RebuildAsync(string tenantId, CancellationToken cancellationToken)
    {
        var existing = (await vectors.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();
        foreach (var vector in existing) await vectors.DeleteAsync(vector.ClaimId, cancellationToken);
        var tenantClaims = (await claims.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.Status == "Active").OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        foreach (var claim in tenantClaims) await IndexClaimAsync(claim, cancellationToken);
        return tenantClaims.Length;
    }

    public async Task<IReadOnlyList<string>> FindConflictsAsync(string claimId, string tenantId,
        CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (claim.Status is not ("Active" or "Disputed")) return [];
        await IndexClaimAsync(claim, cancellationToken);
        var vector = await vectors.GetAsync(claimId, cancellationToken);
        if (vector is null) return [];
        var candidates = (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId
            && x.Id != claimId && x.Scope == claim.Scope && x.Status is "Active" or "Disputed").ToArray();
        var conflicts = new List<string>();
        foreach (var candidate in candidates)
        {
            await IndexClaimAsync(candidate, cancellationToken);
            var candidateVector = await vectors.GetAsync(candidate.Id, cancellationToken);
            if (candidateVector is not null && candidate.Content != claim.Content
                && LocalEmbeddingProvider.Cosine(vector.Vector, candidateVector.Vector) > ConflictThreshold)
                conflicts.Add(candidate.Id);
        }
        return conflicts;
    }

    public Task<MemorySearchResult> SearchHybridAsync(string tenantId, string query, string? scope,
        int maxResults, CancellationToken cancellationToken) =>
        SearchHybridAsync(tenantId, query, scope, maxResults, null, cancellationToken);

    public async Task<MemorySearchResult> SearchHybridAsync(string tenantId, string query, string? scope,
        int maxResults, string? viewer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new InvalidOperationException("Search query is required.");
        if (maxResults is < 1 or > 50) throw new InvalidOperationException("maxResults must be between 1 and 50.");
        var candidates = (await claims.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.Status is "Active" or "Disputed"
                && (x.Scope != "personal" || x.Author == viewer)
                && (scope is null || x.Scope == scope))
            .OrderBy(x => x.Id, StringComparer.Ordinal).Take(200).ToArray();
        foreach (var claim in candidates) await IndexClaimAsync(claim, cancellationToken);

        var queryVector = LocalEmbeddingProvider.Embed(query);
        var queryTokens = LocalEmbeddingProvider.Tokenize(query).ToHashSet(StringComparer.Ordinal);
        var vectorMap = new Dictionary<string, MemoryVectorRecord>(StringComparer.Ordinal);
        foreach (var claim in candidates)
        {
            var vector = await vectors.GetAsync(claim.Id, cancellationToken);
            if (vector is not null) vectorMap[claim.Id] = vector;
        }
        var activeFindings = (await findings.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.Status is "Open" or "Appealed" or "Quarantined").ToArray();

        var scored = candidates.Select(claim =>
        {
            var vectorScore = vectorMap.TryGetValue(claim.Id, out var vector)
                ? Math.Max(0, LocalEmbeddingProvider.Cosine(queryVector, vector.Vector)) : 0;
            var contentTokens = LocalEmbeddingProvider.Tokenize(claim.Content).ToHashSet(StringComparer.Ordinal);
            var keywordScore = queryTokens.Count == 0 ? 0 : (double)queryTokens.Intersect(contentTokens).Count() / queryTokens.Count;
            if (claim.Content.Contains(query, StringComparison.OrdinalIgnoreCase)) keywordScore = 1;
            var claimFindings = activeFindings.Where(x => x.ClaimId == claim.Id).ToArray();
            var reviewFactor = claimFindings.Any(x => x.RiskLevel == "High") ? 0.25
                : claimFindings.Any(x => x.RiskLevel == "Medium") ? 0.65 : 1;
            var ageDays = Math.Max(0, (clock.GetUtcNow() - (claim.LastAccessedAt ?? claim.CreatedAt ?? clock.GetUtcNow())).TotalDays);
            var recency = 1 / (1 + ageDays * 0.01);
            var metadata = claim.Confidence * (claim.Verified ? 1 : 0.85) * (1 + Math.Min(claim.RecallCount, 10) * 0.01);
            var score = (vectorScore * 0.55 + keywordScore * 0.35 + metadata * 0.10) * recency * reviewFactor;
            return (Claim: claim, Vector: vectorScore, Keyword: keywordScore, Score: score, Findings: claimFindings);
        }).ToArray();

        var graphExpanded = new HashSet<string>(StringComparer.Ordinal);
        var graphBoosts = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var seed in scored.OrderByDescending(x => x.Score).Take(3))
        {
            var related = await graph.QueryAsync(tenantId, seed.Claim.Id, 2, 20, cancellationToken);
            foreach (var node in related.Nodes)
            {
                if (node.ClaimId == seed.Claim.Id || graphExpanded.Contains(node.ClaimId)) continue;
                if (!scored.Any(x => x.Claim.Id == node.ClaimId)) continue;
                graphExpanded.Add(node.ClaimId);
                var boost = seed.Score * 0.35 + seed.Vector * 0.10;
                graphBoosts[node.ClaimId] = Math.Max(graphBoosts.GetValueOrDefault(node.ClaimId), boost);
            }
        }

        var conflicts = FindConflicts(scored.Select(x => x.Claim).ToArray(), vectorMap);
        var hits = scored.Where(x => x.Score + graphBoosts.GetValueOrDefault(x.Claim.Id) >= MinimumRecallScore)
            .OrderByDescending(x => x.Score + graphBoosts.GetValueOrDefault(x.Claim.Id))
            .Take(maxResults).Select(x => new MemorySearchHit(
                x.Claim.Id, x.Claim.Content, x.Claim.Scope, x.Claim.SourceRef, x.Claim.Confidence,
                x.Claim.Taint, x.Claim.Version, x.Score + graphBoosts.GetValueOrDefault(x.Claim.Id), x.Vector, x.Keyword,
                graphExpanded.Contains(x.Claim.Id), conflicts.GetValueOrDefault(x.Claim.Id) ?? [],
                x.Findings.Length > 0, x.Findings.Select(finding => finding.RuleId).Distinct(StringComparer.Ordinal).ToArray())).ToArray();
        foreach (var hit in hits)
        {
            var claim = candidates.First(x => x.Id == hit.ClaimId);
            await claims.PutAsync(claim.Id, claim with { LastAccessedAt = clock.GetUtcNow(), RecallCount = claim.RecallCount + 1 }, cancellationToken);
        }
        return new MemorySearchResult(hits, candidates.Length, LocalEmbeddingProvider.ModelVersion);
    }

    private static Dictionary<string, List<string>> FindConflicts(IReadOnlyList<MemoryClaim> claims,
        IReadOnlyDictionary<string, MemoryVectorRecord> vectors)
    {
        var conflicts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var group in claims.GroupBy(x => x.Scope))
        {
            var items = group.ToArray();
            for (var i = 0; i < items.Length; i++)
            for (var j = i + 1; j < items.Length; j++)
            {
                if (items[i].Content == items[j].Content
                    || !vectors.TryGetValue(items[i].Id, out var left)
                    || !vectors.TryGetValue(items[j].Id, out var right)
                    || LocalEmbeddingProvider.Cosine(left.Vector, right.Vector) <= ConflictThreshold) continue;
                AddConflict(conflicts, items[i].Id, items[j].Id);
                AddConflict(conflicts, items[j].Id, items[i].Id);
            }
        }
        return conflicts;
    }

    private static void AddConflict(Dictionary<string, List<string>> conflicts, string claimId, string conflictId)
    {
        if (!conflicts.TryGetValue(claimId, out var list)) conflicts[claimId] = list = [];
        if (!list.Contains(conflictId, StringComparer.Ordinal)) list.Add(conflictId);
    }
}
