using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Runtime;

public sealed record RunnerRegistration(string RunnerId, IReadOnlyList<string> Capabilities);
public sealed record RunnerHeartbeat(string RunnerId, string Status, int ActiveTasks);
public sealed record TaskClaim(Guid TaskId, string Title, string TaskType, Guid ProjectId, string LeaseToken, DateTimeOffset LeaseExpiresAt);
public sealed record TaskCheckpoint(string LeaseToken, string CheckpointJson);
public sealed record TaskCompletion(string LeaseToken, string Output, string Status, IReadOnlyList<string> EvidenceRefs);
