namespace RegentAgent.Contracts.Collaboration;

public sealed record CollaborationNodeDto(
    string Id,
    string Type,
    string Label,
    string Subtitle,
    string Status,
    string? EmployeeId,
    string? ConversationId,
    string? ProjectId,
    string? PositionId,
    string? ParentId,
    int Depth,
    int X,
    int Y,
    IReadOnlyList<string> MemberEmployeeIds,
    int ActivityCount);

public sealed record CollaborationEdgeDto(
    string Id,
    string SourceId,
    string TargetId,
    string Kind,
    int Weight,
    string Label);

public sealed record CollaborationActivityDto(
    string Id,
    DateTimeOffset OccurredAt,
    string Type,
    string Title,
    string Actor,
    string ActorType,
    string SourceType,
    string SourceId,
    string? EmployeeId,
    string? ConversationId,
    string? ProjectId,
    string? TaskId,
    IReadOnlyList<string> EvidenceRefs,
    string? AuditHash);

public sealed record CollaborationSummaryDto(
    int EmployeeCount,
    int ConversationCount,
    int MessageCount,
    int CollaborationCount,
    int ProjectCount,
    int ActivityCount);

public sealed record CollaborationGraphDto(
    IReadOnlyList<CollaborationNodeDto> Nodes,
    IReadOnlyList<CollaborationEdgeDto> Edges,
    IReadOnlyList<CollaborationActivityDto> Activities,
    CollaborationSummaryDto Summary,
    DateTimeOffset GeneratedAt);

public sealed record CollaborationTraceDto(
    string EntityType,
    string EntityId,
    IReadOnlyList<CollaborationActivityDto> Activities,
    DateTimeOffset GeneratedAt);
