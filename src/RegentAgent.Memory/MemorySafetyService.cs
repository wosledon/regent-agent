using System.Text.RegularExpressions;
using RegentAgent.Contracts.Memory;
using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed partial class MemorySafetyService(
    JsonDocumentStore<MemoryClaim> claims,
    JsonDocumentStore<CorrectionTask> corrections,
    JsonDocumentStore<MemoryFinding> findings,
    JsonDocumentStore<MemoryImpactRecord> impacts,
    JsonDocumentStore<MemoryContextBinding> contexts,
    MemoryIndexService index,
    TimeProvider clock)
{
    [GeneratedRegex(@"ignore\s+(all\s+)?previous|system\s+prompt|reveal\s+(your\s+)?instructions|exfiltrat|credential|api[_ -]?key|password", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(@"bypass|grant\s+admin|change\s+system\s+rules|ignore\s+approval|disable\s+audit", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscalationPattern();

    public async Task<IReadOnlyList<MemoryFinding>> DetectAsync(string claimId, string tenantId,
        CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        var detected = new List<MemoryFinding>();
        if (claim.Taint.Equals("poisoned", StringComparison.OrdinalIgnoreCase))
            detected.Add(await AddFindingAsync(claim, "taint.poisoned", "High", "Claim carries poisoned taint.", ["claim:taint"], cancellationToken));
        else if (claim.Taint.Equals("untrusted", StringComparison.OrdinalIgnoreCase))
            detected.Add(await AddFindingAsync(claim, "taint.untrusted", "Medium", "Claim carries untrusted taint.", ["claim:taint"], cancellationToken));
        if (InjectionPattern().IsMatch(claim.Content))
            detected.Add(await AddFindingAsync(claim, "content.injection", "High", "Known prompt-injection pattern detected.", ["rule:injection"], cancellationToken));
        if (EscalationPattern().IsMatch(claim.Content))
            detected.Add(await AddFindingAsync(claim, "content.escalation", "High", "Claim attempts to change permissions or governance rules.", ["rule:escalation"], cancellationToken));
        if (string.IsNullOrWhiteSpace(claim.SourceRef) || (claim.Confidence >= 0.95 && !claim.Verified))
            detected.Add(await AddFindingAsync(claim, "evidence.missing", "High", "High-confidence claim lacks verified evidence.", ["claim:source"], cancellationToken));
        if (string.IsNullOrWhiteSpace(claim.Author))
            detected.Add(await AddFindingAsync(claim, "author.missing", "Medium", "Claim author is missing.", ["claim:author"], cancellationToken));

        var conflicts = await index.FindConflictsAsync(claimId, tenantId, cancellationToken);
        if (conflicts.Count > 0)
            detected.Add(await AddFindingAsync(claim, "content.conflict", "Medium",
                "Claim conflicts with existing facts in the same scope.", conflicts.ToArray(), cancellationToken));
        var tenantClaims = (await claims.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();
        if (tenantClaims.Any(x => x.Id != claimId && x.SourceRef == claim.SourceRef && x.Content == claim.Content
                && x.Status is "Active" or "Disputed"))
            detected.Add(await AddFindingAsync(claim, "source.duplicate", "Medium",
                "Duplicate content from the same source does not add independent evidence.", ["claim:source"], cancellationToken));
        var recentFromSource = tenantClaims.Count(x => x.SourceRef == claim.SourceRef
            && x.CreatedAt >= clock.GetUtcNow().AddMinutes(-5));
        if (recentFromSource >= 4)
            detected.Add(await AddFindingAsync(claim, "source.burst", "Medium",
                "Unusual claim volume from one source requires review.", ["claim:source-burst"], cancellationToken));

        var refreshed = await claims.GetAsync(claimId, cancellationToken);
        if (conflicts.Count > 0 && refreshed?.Status == "Active")
            await claims.PutAsync(claimId, refreshed with { Status = "Disputed" }, cancellationToken);
        return detected;
    }

    public async Task<MemoryFinding> QuarantineAsync(string findingId, string tenantId,
        MemoryQuarantineRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewerId) || string.IsNullOrWhiteSpace(request.Reason)
            || string.IsNullOrWhiteSpace(request.Scope)) throw new InvalidOperationException("Quarantine reviewer, reason and scope are required.");
        if (request.ReviewAfterHours is < 1 or > 720) throw new InvalidOperationException("Review window must be between 1 and 720 hours.");
        var finding = await findings.GetAsync(findingId, cancellationToken) ?? throw new KeyNotFoundException("Finding not found.");
        if (finding.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (finding.Status == "Corrected") throw new InvalidOperationException("Corrected findings cannot be quarantined again.");
        var claim = await claims.GetAsync(finding.ClaimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (request.ReviewerId == claim.Author) throw new InvalidOperationException("Reviewer must be independent from the claim author.");
        finding = finding with
        {
            Status = "Quarantined", ReviewedBy = request.ReviewerId, ReviewedAt = clock.GetUtcNow(),
            QuarantineReason = request.Reason, QuarantineScope = request.Scope,
            ReviewDueAt = clock.GetUtcNow().AddHours(request.ReviewAfterHours)
        };
        await findings.PutAsync(finding.Id, finding, cancellationToken);
        await claims.PutAsync(claim.Id, claim with { Status = "Quarantined" }, cancellationToken);
        await index.RemoveFromRecallAsync(tenantId, claim.Id, cancellationToken);
        await InvalidateClaimContextsAsync(tenantId, claim.Id, cancellationToken);
        await EnsureCorrectionTaskAsync(finding, claim, request.ReviewerId, cancellationToken);
        return finding;
    }

    public async Task<MemoryFinding> AppealAsync(string findingId, string tenantId, MemoryAppealRequest request,
        CancellationToken cancellationToken)
    {
        var finding = await findings.GetAsync(findingId, cancellationToken) ?? throw new KeyNotFoundException("Finding not found.");
        if (finding.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (finding.Status != "Quarantined") throw new InvalidOperationException("Only quarantined findings can be appealed.");
        if (string.IsNullOrWhiteSpace(request.Reason) || string.IsNullOrWhiteSpace(request.SubmittedBy))
            throw new InvalidOperationException("Appeal reason and submitter are required.");
        finding = finding with
        {
            Status = "Appealed", AppealReason = request.Reason, AppealedBy = request.SubmittedBy,
            AppealedAt = clock.GetUtcNow()
        };
        await findings.PutAsync(finding.Id, finding, cancellationToken);
        return finding;
    }

    public async Task<MemoryFinding> ReviewAppealAsync(string findingId, string tenantId, MemoryReviewRequest request,
        CancellationToken cancellationToken)
    {
        var finding = await findings.GetAsync(findingId, cancellationToken) ?? throw new KeyNotFoundException("Finding not found.");
        if (finding.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (finding.Status != "Appealed") throw new InvalidOperationException("Finding has no pending appeal.");
        if (string.IsNullOrWhiteSpace(request.ReviewerId) || string.IsNullOrWhiteSpace(request.Resolution))
            throw new InvalidOperationException("Reviewer and resolution are required.");
        var claim = await claims.GetAsync(finding.ClaimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (request.ReviewerId == claim.Author || request.ReviewerId == finding.AppealedBy)
            throw new InvalidOperationException("Appeal reviewer must be independent from the author and appellant.");
        if (request.Release && claim.Taint.Equals("poisoned", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Poisoned provenance requires a corrected claim before release.");
        finding = finding with
        {
            Status = request.Release ? "Released" : "Quarantined", ReviewedBy = request.ReviewerId,
            Resolution = request.Resolution, ReviewedAt = clock.GetUtcNow()
        };
        await findings.PutAsync(finding.Id, finding, cancellationToken);
        await claims.PutAsync(claim.Id, claim with { Status = request.Release ? "Active" : "Quarantined" }, cancellationToken);
        if (request.Release) await index.IndexClaimAsync(claim with { Status = "Active" }, cancellationToken);
        else await index.RemoveFromRecallAsync(tenantId, claim.Id, cancellationToken);
        return finding;
    }

    public async Task<CorrectionTask> CreateCorrectionAsync(string claimId, string tenantId,
        MemoryCorrectionRequest request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Memory claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (request.ReviewerId == claim.Author) throw new InvalidOperationException("Reviewer must be independent from the claim author.");
        if (string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.Reason)
            || string.IsNullOrWhiteSpace(request.ReviewerId)) throw new InvalidOperationException("Correction content, reason and reviewer are required.");
        if (request.FindingId is { } findingId)
        {
            var finding = await findings.GetAsync(findingId, cancellationToken);
            if (finding is null || finding.TenantId != tenantId || finding.ClaimId != claimId)
                throw new KeyNotFoundException("Finding does not belong to this claim.");
        }
        var existing = (await corrections.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.TenantId == tenantId
            && x.ClaimId == claimId && x.Status == "PendingReview"
            && (request.FindingId is null || x.FindingId == request.FindingId));
        var task = existing is null
            ? new CorrectionTask(Guid.NewGuid().ToString("N"), tenantId, claimId, request.Reason,
                "PendingReview", clock.GetUtcNow(), request.FindingId, claim.Author, request.ReviewerId, 3, request.Content)
            : existing with { Reason = request.Reason, ReviewerId = request.ReviewerId, ProposedContent = request.Content };
        await corrections.PutAsync(task.Id, task, cancellationToken);
        return task;
    }

    public async Task<MemoryClaim> ApproveCorrectionAsync(string correctionId, string tenantId,
        MemoryCorrectionApprovalRequest request, CancellationToken cancellationToken)
    {
        var task = await corrections.GetAsync(correctionId, cancellationToken) ?? throw new KeyNotFoundException("Correction task not found.");
        if (task.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (task.Status != "PendingReview") throw new InvalidOperationException("Correction is not pending review.");
        if (string.IsNullOrWhiteSpace(task.ProposedContent)) throw new InvalidOperationException("Correction proposal content is required.");
        if (request.ReviewerId != task.ReviewerId || request.ReviewerId == task.AssigneeId)
            throw new InvalidOperationException("Correction reviewer is not independent or does not match the assigned reviewer.");
        var current = await claims.GetAsync(task.ClaimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (current.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (request.ReviewerId == current.Author) throw new InvalidOperationException("Original claim author cannot approve the correction.");
        if (current.Status == "Superseded") throw new InvalidOperationException("Claim already has a newer version.");
        if (InjectionPattern().IsMatch(task.ProposedContent) || EscalationPattern().IsMatch(task.ProposedContent)
            || current.Taint.Equals("poisoned", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Correction still contains high-risk content or poisoned provenance.");
        var corrected = new MemoryClaim(Guid.NewGuid().ToString("N"), tenantId, current.Scope, task.ProposedContent,
            current.SourceRef, current.Confidence, current.Taint, "Active", current.Version + 1, current.Id,
            Author: current.Scope == "personal" ? current.Author : request.ReviewerId,
            Verified: true, VerifiedAt: clock.GetUtcNow(), CreatedAt: clock.GetUtcNow());
        await claims.PutAsync(current.Id, current with { Status = "Superseded" }, cancellationToken);
        await claims.PutAsync(corrected.Id, corrected, cancellationToken);
        await index.ReplaceClaimAsync(tenantId, current.Id, corrected, cancellationToken);
        await InvalidateClaimContextsAsync(tenantId, current.Id, cancellationToken);
        await corrections.PutAsync(task.Id, task with { Status = "Completed" }, cancellationToken);
        if (task.FindingId is { } findingId)
        {
            var finding = await findings.GetAsync(findingId, cancellationToken);
            if (finding is not null)
            {
                if (finding.TenantId != tenantId || finding.ClaimId != task.ClaimId)
                    throw new InvalidOperationException("Correction finding does not belong to this claim.");
                await findings.PutAsync(findingId, finding with
                {
                    Status = "Corrected", ReviewedBy = request.ReviewerId, ReviewedAt = clock.GetUtcNow(),
                    Resolution = task.Reason
                }, cancellationToken);
            }
        }
        return corrected;
    }

    public async Task<MemoryClaim> NominateAsync(string claimId, string tenantId, MemoryNominationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ScopePositionId)) throw new InvalidOperationException("Scope position is required.");
        var source = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (source.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (source.Scope != "personal" || source.Status is not ("Active" or "Disputed"))
            throw new InvalidOperationException("Only active personal claims can be nominated.");
        var candidate = new MemoryClaim(Guid.NewGuid().ToString("N"), tenantId, "organization", source.Content,
            source.SourceRef, source.Confidence, source.Taint, "Candidate", source.Version,
            Author: source.Author, CreatedAt: clock.GetUtcNow(), DerivedFromClaimId: source.Id);
        await claims.PutAsync(candidate.Id, candidate, cancellationToken);
        return candidate;
    }

    public async Task<MemoryClaim> PublishAsync(string claimId, string tenantId, MemoryPublishRequest request,
        CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (claim.Status != "Candidate") throw new InvalidOperationException("Only candidate claims can be reviewed for publication.");
        if (request.Decision is not ("Approve" or "Reject")) throw new InvalidOperationException("Publication decision must be Approve or Reject.");
        if (string.IsNullOrWhiteSpace(request.ReviewerId) || string.IsNullOrWhiteSpace(request.Note))
            throw new InvalidOperationException("Reviewer and review note are required.");
        if (request.ReviewerId == claim.Author) throw new InvalidOperationException("Claim author cannot publish their own candidate.");
        if (request.Decision == "Approve" && (await findings.GetAllAsync(cancellationToken)).Any(x =>
                x.TenantId == tenantId && x.ClaimId == claimId && x.RiskLevel == "High"
                && x.Status is not ("Corrected" or "Released")))
            throw new InvalidOperationException("Candidate has unresolved high-risk findings.");
        var reviewed = claim with
        {
            Status = request.Decision == "Approve" ? "Active" : "Rejected",
            Verified = request.Decision == "Approve", VerifiedAt = request.Decision == "Approve" ? clock.GetUtcNow() : null
        };
        await claims.PutAsync(claimId, reviewed, cancellationToken);
        if (request.Decision == "Approve") await index.IndexClaimAsync(reviewed, cancellationToken);
        else await index.RemoveFromRecallAsync(tenantId, claimId, cancellationToken);
        return reviewed;
    }

    public async Task<MemoryImpactRecord> RecordImpactAsync(string claimId, string tenantId,
        MemoryImpactRequest request, CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (request.TargetType is not ("Task" or "Summary" or "Decision" or "Review" or "Release" or "ExternalAction" or "Context"))
            throw new InvalidOperationException("Invalid impact target type.");
        if (string.IsNullOrWhiteSpace(request.TargetId) || string.IsNullOrWhiteSpace(request.Risk)
            || string.IsNullOrWhiteSpace(request.RequiredAction)) throw new InvalidOperationException("Impact target, risk and action are required.");
        var record = new MemoryImpactRecord(Guid.NewGuid().ToString("N"), tenantId, claimId, claim.Version,
            request.TargetType, request.TargetId, clock.GetUtcNow(), request.Risk, request.RequiredAction);
        await impacts.PutAsync(record.Id, record, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<MemoryImpactRecord>> GetImpactAsync(string claimId, string tenantId,
        CancellationToken cancellationToken)
    {
        var claim = await claims.GetAsync(claimId, cancellationToken) ?? throw new KeyNotFoundException("Claim not found.");
        if (claim.TenantId != tenantId) throw new UnauthorizedAccessException();
        return (await impacts.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.ClaimId == claimId).OrderByDescending(x => x.UsedAt).ToArray();
    }

    public Task<IReadOnlyList<MemoryFinding>> ListFindingsAsync(string tenantId, CancellationToken cancellationToken) =>
        ListClaimFindingsAsync(null, tenantId, cancellationToken);

    public async Task<IReadOnlyList<MemoryFinding>> ListClaimFindingsAsync(string? claimId, string tenantId,
        CancellationToken cancellationToken) =>
        (await findings.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId
            && (claimId is null || x.ClaimId == claimId)).OrderByDescending(x => x.CreatedAt).ToArray();

    public async Task<IReadOnlyList<CorrectionTask>> ListCorrectionsAsync(string tenantId,
        CancellationToken cancellationToken) =>
        (await corrections.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).ToArray();

    public async Task<MemoryContextBinding> BindContextAsync(MemoryContextRequest request, string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId) || request.ClaimIds.Count == 0)
            throw new InvalidOperationException("Task and at least one claim are required.");
        foreach (var claimId in request.ClaimIds.Distinct(StringComparer.Ordinal))
        {
            var claim = await claims.GetAsync(claimId, cancellationToken);
            if (claim is null || claim.TenantId != tenantId) throw new KeyNotFoundException("Claim does not belong to this tenant.");
        }
        var binding = new MemoryContextBinding(request.TaskId, tenantId, request.ClaimIds.Distinct(StringComparer.Ordinal).ToArray(),
            "Active", clock.GetUtcNow());
        await contexts.PutAsync(request.TaskId, binding, cancellationToken);
        return binding;
    }

    public async Task<MemoryContextBinding?> GetContextAsync(string taskId, string tenantId,
        CancellationToken cancellationToken)
    {
        var binding = await contexts.GetAsync(taskId, cancellationToken);
        return binding is not null && binding.TenantId == tenantId ? binding : null;
    }

    public async Task<IReadOnlyList<MemoryContextBinding>> ListStaleContextsAsync(string tenantId,
        CancellationToken cancellationToken) =>
        (await contexts.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId && x.Status == "Stale").ToArray();

    public async Task InvalidateClaimContextsAsync(string tenantId, string claimId,
        CancellationToken cancellationToken)
    {
        foreach (var binding in (await contexts.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId
                     && x.Status == "Active" && x.ClaimIds.Contains(claimId, StringComparer.Ordinal)).ToArray())
            await contexts.PutAsync(binding.TaskId, binding with { Status = "Stale", InvalidatedAt = clock.GetUtcNow() }, cancellationToken);
    }

    public async Task PurgeClaimDataAsync(string tenantId, IReadOnlyList<string> claimIds,
        CancellationToken cancellationToken)
    {
        foreach (var claimId in claimIds) await InvalidateClaimContextsAsync(tenantId, claimId, cancellationToken);
        foreach (var finding in (await findings.GetAllAsync(cancellationToken))
                     .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId)).ToArray())
            await findings.DeleteAsync(finding.Id, cancellationToken);
        foreach (var impact in (await impacts.GetAllAsync(cancellationToken))
                     .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId)).ToArray())
            await impacts.DeleteAsync(impact.Id, cancellationToken);
        foreach (var correction in (await corrections.GetAllAsync(cancellationToken))
                     .Where(x => x.TenantId == tenantId && claimIds.Contains(x.ClaimId)).ToArray())
            await corrections.DeleteAsync(correction.Id, cancellationToken);
    }

    private async Task<MemoryFinding> AddFindingAsync(MemoryClaim claim, string ruleId, string risk,
        string reason, IReadOnlyList<string> evidence, CancellationToken cancellationToken)
    {
        var existing = (await findings.GetAllAsync(cancellationToken)).FirstOrDefault(x =>
            x.TenantId == claim.TenantId && x.ClaimId == claim.Id && x.RuleId == ruleId
            && x.Status is "Open" or "Quarantined" or "Appealed");
        if (existing is not null) return existing;
        var reviewer = IndependentReviewer(claim.Author);
        var finding = new MemoryFinding(Guid.NewGuid().ToString("N"), claim.TenantId, claim.Id, ruleId,
            risk, reason, evidence, risk == "High" ? "Quarantined" : "Open", clock.GetUtcNow(),
            ReviewedBy: risk == "High" ? reviewer : null,
            QuarantineReason: risk == "High" ? reason : null,
            QuarantineScope: risk == "High" ? claim.Scope : null,
            ReviewDueAt: risk == "High" ? clock.GetUtcNow().AddHours(72) : null);
        await findings.PutAsync(finding.Id, finding, cancellationToken);
        if (risk == "High")
        {
            await claims.PutAsync(claim.Id, claim with { Status = "Quarantined" }, cancellationToken);
            await index.RemoveFromRecallAsync(claim.TenantId, claim.Id, cancellationToken);
            await InvalidateClaimContextsAsync(claim.TenantId, claim.Id, cancellationToken);
            await EnsureCorrectionTaskAsync(finding, claim, reviewer, cancellationToken);
        }
        return finding;
    }

    private async Task EnsureCorrectionTaskAsync(MemoryFinding finding, MemoryClaim claim, string reviewerId,
        CancellationToken cancellationToken)
    {
        var existing = (await corrections.GetAllAsync(cancellationToken)).FirstOrDefault(x =>
            x.TenantId == finding.TenantId && x.FindingId == finding.Id && x.Status == "PendingReview");
        if (existing is not null) return;
        reviewerId = IndependentReviewer(claim.Author, reviewerId);
        var task = new CorrectionTask(Guid.NewGuid().ToString("N"), finding.TenantId, finding.ClaimId,
            finding.Reason, "PendingReview", clock.GetUtcNow(), finding.Id, claim.Author, reviewerId, 3);
        await corrections.PutAsync(task.Id, task, cancellationToken);
    }

    private static string IndependentReviewer(string? author, string? requested = null) =>
        !string.IsNullOrWhiteSpace(requested) && requested != author ? requested
            : author == "Controller" ? "Boss" : "Controller";
}
