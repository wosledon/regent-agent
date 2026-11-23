namespace RegentAgent.Contracts.Memory;

public sealed record MemoryFinding(
    string Id,
    string TenantId,
    string ClaimId,
    string RuleId,
    string RiskLevel,
    string Reason,
    IReadOnlyList<string> EvidenceRefs,
    string Status,
    DateTimeOffset CreatedAt,
    string? ReviewedBy = null,
    string? Resolution = null,
    string? QuarantineReason = null,
    string? QuarantineScope = null,
    DateTimeOffset? ReviewDueAt = null,
    string? AppealReason = null,
    string? AppealedBy = null,
    DateTimeOffset? AppealedAt = null,
    DateTimeOffset? ReviewedAt = null);

public sealed record MemoryImpactRecord(
    string Id,
    string TenantId,
    string ClaimId,
    int ClaimVersion,
    string TargetType,
    string TargetId,
    DateTimeOffset UsedAt,
    string Risk,
    string RequiredAction);

public sealed record MemorySearchHit(
    string ClaimId,
    string Content,
    string Scope,
    string SourceRef,
    double Confidence,
    string Taint,
    int Version,
    double Score,
    double VectorScore,
    double KeywordScore,
    bool GraphExpanded,
    IReadOnlyList<string> ConflictWithIds,
    bool ReviewRequired,
    IReadOnlyList<string> FindingRules);

public sealed record MemorySearchResult(IReadOnlyList<MemorySearchHit> Hits, int Considered, string IndexVersion);

public sealed record MemoryCaptureRequest(string Scope, string Content, string SourceRef, double Confidence, string Taint);
public sealed record MemoryDetectRequest(string ClaimId);
public sealed record MemoryQuarantineRequest(string ReviewerId, string Reason, string Scope, int ReviewAfterHours = 72);
public sealed record MemoryCorrectionRequest(string ClaimId, string Content, string Reason, string ReviewerId, string? FindingId = null);
public sealed record MemoryCorrectionApprovalRequest(string ReviewerId);
public sealed record MemoryImpactRequest(string TargetType, string TargetId, string Risk, string RequiredAction);
public sealed record MemoryAppealRequest(string Reason, string SubmittedBy);
public sealed record MemoryReviewRequest(string ReviewerId, string Resolution, bool Release);
public sealed record MemoryPublishRequest(string ReviewerId, string Decision, string Note);
