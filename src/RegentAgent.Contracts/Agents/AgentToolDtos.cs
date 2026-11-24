namespace RegentAgent.Contracts.Agents;

public sealed record AgentToolInvocationRequest(
    string ToolName,
    string Version,
    string Capability,
    string TaskGrant,
    string Taint,
    string ArgumentsJson,
    string? IdempotencyKey = null);

public sealed record AgentToolInvocationResult(
    bool Allowed,
    bool Success,
    string? Reason,
    string? Digest,
    object? Result,
    string? AuditHash);
