using RegentAgent.Contracts.Pagination;

namespace RegentAgent.Contracts.Collaboration;

public sealed record CreateTaskDelegationRequest(
    Guid TaskId,
    Guid DelegateEmployeeId,
    int Weight,
    string AcceptanceCriteria,
    IReadOnlyList<string> EvidenceRequirements,
    IReadOnlyList<string> RequiredCapabilities,
    string Reason);

public sealed record CoordinationDecisionRequest(bool Accepted, string Reason);

public sealed record CoordinationCancelRequest(string Reason);

public sealed record CoordinationResolutionRequest(string Resolution);

public sealed record HandoffPackageDto(
    string Goal,
    string AcceptanceCriteria,
    string CheckpointJson,
    IReadOnlyList<string> CompletedItems,
    IReadOnlyList<string> RemainingItems,
    IReadOnlyList<string> ArtifactRefs,
    IReadOnlyList<string> Decisions,
    IReadOnlyList<string> RisksAndCounterEvidence,
    IReadOnlyList<string> Blockers,
    string NextAction);

public sealed record CreateHandoffRequest(
    Guid TaskId,
    Guid RecipientEmployeeId,
    string LeaseToken,
    HandoffPackageDto Package);

public sealed record CreateHelpRequest(
    Guid TaskId,
    Guid HelperEmployeeId,
    string Question,
    IReadOnlyList<string> AttemptedSteps,
    IReadOnlyList<string> EvidenceRefs,
    string ExpectedAnswer,
    DateTimeOffset Deadline);

public sealed record HelpResponseRequest(string ResponseType, string Answer, Guid? RedirectEmployeeId = null);

public sealed record HelpContextRequest(string Context, IReadOnlyList<string> EvidenceRefs);

public sealed record CreateInterruptRequest(Guid TaskId, string Priority, string Reason);

public sealed record InterruptCheckpointRequest(string LeaseToken, string CheckpointJson);

public sealed record InterruptResolutionRequest(string Resolution);

public sealed record InterruptResumeRequest(string LeaseToken);

public sealed record InterruptResumeResult(TaskCoordinationDto Coordination, string LeaseToken, DateTimeOffset LeaseExpiresAt);

public sealed record TaskCoordinationDto(
    Guid Id,
    Guid TenantId,
    Guid TaskId,
    Guid ProjectId,
    string Kind,
    string Status,
    Guid RequesterEmployeeId,
    Guid TargetEmployeeId,
    string Priority,
    string Summary,
    string DetailsJson,
    string? ResponseJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? Deadline,
    DateTimeOffset? CompletedAt,
    int Version);

public sealed record CoordinationPageQuery
{
    public int Limit { get; init; } = 50;
    public string? Cursor { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
    public Guid? EmployeeId { get; init; }
    public bool? IncomingOnly { get; init; }
    public int NormalizedLimit => Math.Clamp(Limit, 1, 200);
}
