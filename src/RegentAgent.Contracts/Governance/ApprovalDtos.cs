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
    IReadOnlyList<string> EvidenceRefs);

public sealed record ApprovalDecision(string Decision, string Reason);
