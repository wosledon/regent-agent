using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed record MemoryClaim(string Id, string TenantId, string Scope, string Content, string SourceRef, double Confidence, string Taint, string Status, int Version, string? SupersedesId = null,
    DateTimeOffset? LastAccessedAt = null, int RecallCount = 0, string? Author = null, bool Verified = false,
    DateTimeOffset? VerifiedAt = null, DateTimeOffset? CreatedAt = null, string? DerivedFromClaimId = null,
    string? AuthorEmployeeId = null, string? AuthorUserId = null, string? AuthorEmploymentPeriodId = null);
public sealed record CorrectionTask(string Id, string TenantId, string ClaimId, string Reason, string Status, DateTimeOffset CreatedAt,
    string? FindingId = null, string? AssigneeId = null, string? ReviewerId = null, int Weight = 2,
    string? ProposedContent = null);

public sealed class MemoryService(JsonDocumentStore<MemoryClaim> claims, JsonDocumentStore<CorrectionTask> corrections,
    MemoryIndexService? index = null, MemorySafetyService? safety = null) : IPersonalMemoryIsolation
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await claims.GetAllAsync(cancellationToken)).Count > 0) return;
        var seed = new MemoryClaim("memory-1", "11111111-1111-1111-1111-111111111111", "organization",
            "Provider contract tests must cover native streaming errors.", "decision:provider-contract", 0.92, "trusted", "Active", 1,
            Author: "system", Verified: true, VerifiedAt: DateTimeOffset.UtcNow, CreatedAt: DateTimeOffset.UtcNow);
        await claims.PutAsync(seed.Id, seed, cancellationToken);
        if (index is not null) await index.IndexClaimAsync(seed, cancellationToken);
    }

    public async Task<MemoryClaim> CaptureAsync(string tenantId, string scope, string content, string sourceRef,
        double confidence, string taint, string author, CancellationToken cancellationToken, bool requireReview = false,
        string? authorEmployeeId = null, string? authorUserId = null, string? authorEmploymentPeriodId = null)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(author))
            throw new InvalidOperationException("Memory content, source and author are required.");
        if (scope is not ("personal" or "organization" or "knowledge_base"))
            throw new InvalidOperationException("Invalid memory scope.");
        if (taint is not ("trusted" or "untrusted" or "poisoned"))
            throw new InvalidOperationException("Invalid memory taint.");
        if (confidence is < 0 or > 1) throw new InvalidOperationException("Confidence must be in [0, 1].");
        var status = requireReview && scope != "personal" ? "Candidate" : "Active";
        var claim = new MemoryClaim(Guid.NewGuid().ToString("N"), tenantId, scope, content, sourceRef,
            confidence, taint, status, 1, Author: author, CreatedAt: DateTimeOffset.UtcNow,
            AuthorEmployeeId: scope == "personal" ? authorEmployeeId : null,
            AuthorUserId: scope == "personal" ? authorUserId : null,
            AuthorEmploymentPeriodId: scope == "personal" ? authorEmploymentPeriodId : null);
        await claims.PutAsync(claim.Id, claim, cancellationToken);
        if (index is not null) await index.IndexClaimAsync(claim, cancellationToken);
        if (safety is not null) await safety.DetectAsync(claim.Id, tenantId, cancellationToken);
        return await claims.GetAsync(claim.Id, cancellationToken) ?? claim;
    }

    public async Task<MemoryClaim?> GetAsync(string claimId, string tenantId, string? viewer,
        CancellationToken cancellationToken, string? viewerEmployeeId = null, string? viewerUserId = null,
        bool allowLegacyNameFallback = false, string? viewerEmploymentPeriodId = null)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken);
        if (claim is null || claim.TenantId != tenantId || claim.Status != "Active") return null;
        var allowLegacyFallback = claim.Scope == "personal" && allowLegacyNameFallback
            && PersonalMemoryAccess.LegacyNameIsUnambiguous(
                await claims.GetAllAsync(cancellationToken), tenantId, viewer);
        return PersonalMemoryAccess.CanRead(claim, viewer, viewerEmployeeId, viewerUserId, allowLegacyFallback,
            viewerEmploymentPeriodId)
            ? claim
            : null;
    }

    public async Task<IReadOnlyList<MemoryClaim>> SearchAsync(string tenantId, string? query, string? viewer,
        CancellationToken cancellationToken, string? viewerEmployeeId = null, string? viewerUserId = null,
        bool allowLegacyNameFallback = false, string? viewerEmploymentPeriodId = null)
    {
        var tenantClaims = await claims.GetAllAsync(cancellationToken);
        var allowLegacyFallback = allowLegacyNameFallback
            && PersonalMemoryAccess.LegacyNameIsUnambiguous(tenantClaims, tenantId, viewer);
        var matches = tenantClaims.Where(x => x.TenantId == tenantId && x.Status == "Active"
            && PersonalMemoryAccess.CanRead(x, viewer, viewerEmployeeId, viewerUserId, allowLegacyFallback,
                viewerEmploymentPeriodId)
            && (string.IsNullOrWhiteSpace(query) || x.Content.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        foreach (var claim in matches)
            await claims.PutAsync(claim.Id, claim with { LastAccessedAt = DateTimeOffset.UtcNow, RecallCount = claim.RecallCount + 1 }, cancellationToken);
        return matches;
    }

    public Task<MemoryClaim?> GetAsync(string claimId, string tenantId, CancellationToken cancellationToken) =>
        GetAsync(claimId, tenantId, null, cancellationToken);

    public Task<IReadOnlyList<MemoryClaim>> SearchAsync(string tenantId, string? query,
        CancellationToken cancellationToken) => SearchAsync(tenantId, query, null, cancellationToken);

    public async Task<IReadOnlyList<MemoryClaim>> ListReviewQueueAsync(string tenantId,
        CancellationToken cancellationToken) =>
        (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && x.Status == "Candidate")
            .OrderByDescending(x => x.CreatedAt).ToArray();

    public async Task IsolatePersonalClaimsAsync(string tenantId, string author, CancellationToken cancellationToken,
        string? authorEmployeeId = null, string? authorUserId = null, bool allowLegacyNameFallback = false,
        string? authorEmploymentPeriodId = null, DateTimeOffset? createdBefore = null)
    {
        var tenantClaims = await claims.GetAllAsync(cancellationToken);
        var legacyFallback = allowLegacyNameFallback
            && PersonalMemoryAccess.LegacyNameIsUnambiguous(tenantClaims, tenantId, author);
        foreach (var claim in tenantClaims.Where(x => x.TenantId == tenantId
                     && (x.Status == "Isolated"
                         || (x.Status is "Active" or "Disputed" or "Quarantined" or "Candidate"
                             && MatchesTerminatedOwnership(x, author, authorEmployeeId, authorUserId, legacyFallback,
                                 authorEmploymentPeriodId, createdBefore)))).ToArray())
        {
            if (claim.Status != "Isolated")
                await claims.PutAsync(claim.Id, claim with { Status = "Isolated" }, cancellationToken);
            if (index is not null) await index.RemoveFromRecallAsync(tenantId, claim.Id, cancellationToken);
        }
    }

    private static bool MatchesTerminatedOwnership(MemoryClaim claim, string author, string? authorEmployeeId,
        string? authorUserId, bool allowLegacyNameFallback, string? authorEmploymentPeriodId,
        DateTimeOffset? createdBefore)
    {
        if (claim.Scope != "personal") return false;
        if (!string.IsNullOrWhiteSpace(claim.AuthorEmploymentPeriodId))
            return !string.IsNullOrWhiteSpace(authorEmploymentPeriodId)
                && string.Equals(claim.AuthorEmploymentPeriodId, authorEmploymentPeriodId, StringComparison.Ordinal);
        return (createdBefore is null || claim.CreatedAt is null || claim.CreatedAt <= createdBefore)
            && PersonalMemoryAccess.MatchesOwner(claim, author, authorEmployeeId, authorUserId, allowLegacyNameFallback);
    }

    public async Task ArchiveAsync(string claimId, string tenantId, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        await claims.PutAsync(claimId, claim with { Status = "Archived" }, cancellationToken);
        if (index is not null) await index.RemoveFromRecallAsync(tenantId, claimId, cancellationToken);
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
        if (index is not null) await index.RemoveFromRecallAsync(tenantId, claimId, cancellationToken);
    }

    public async Task DeleteAsync(string claimId, string tenantId, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        var tenantClaims = (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();
        var related = new HashSet<string>(StringComparer.Ordinal) { claimId };
        var queue = new Queue<string>();
        queue.Enqueue(claimId);
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            foreach (var child in tenantClaims.Where(x => x.SupersedesId == currentId))
                if (related.Add(child.Id)) queue.Enqueue(child.Id);
        }
        foreach (var id in related)
        {
            await claims.DeleteAsync(id, cancellationToken);
            if (index is not null) await index.DeleteClaimAsync(tenantId, id, cancellationToken);
        }
        if (safety is not null)
        {
            await safety.PurgeClaimDataAsync(tenantId, related.ToArray(), cancellationToken);
        }
        else
        {
            foreach (var correction in (await corrections.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && related.Contains(x.ClaimId)).ToArray())
                await corrections.DeleteAsync(correction.Id, cancellationToken);
        }
    }

}
