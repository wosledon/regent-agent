using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Governance;

public sealed record ApprovalDto(
    Guid Id,
    string ActionType,
    string Title,
    string Impact,
    Severity Severity,
    ApprovalState State,
    string Requester,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> EvidenceRefs,
    Guid? TargetEmployeeId = null,
    Guid? TargetCandidateId = null,
    Guid? TargetPositionId = null,
    DateTimeOffset? ConsumedAt = null,
    Guid? ConsumedBy = null,
    Guid? DecidedBy = null,
    DateTimeOffset? DecidedAt = null);

public sealed record ApprovalDecision(string Decision, string Reason);

public sealed record CreateApprovalDto(
    string ActionType,
    string Title,
    string Impact,
    Severity Severity,
    string Requester,
    int ExpiresInMinutes,
    IReadOnlyList<string> EvidenceRefs,
    Guid? TargetEmployeeId = null,
    Guid? TargetCandidateId = null,
    Guid? TargetPositionId = null);
