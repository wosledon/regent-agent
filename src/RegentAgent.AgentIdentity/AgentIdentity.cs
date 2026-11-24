namespace RegentAgent.AgentIdentity;

public sealed record AgentIdentity(
    Guid TenantId,
    Guid EmployeeId,
    Guid? TaskId,
    string? RunnerId,
    IReadOnlyList<string> Scopes,
    string TokenId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

public sealed record AgentAssignmentRequest(
    Guid TenantId,
    Guid EmployeeId,
    Guid? TaskId,
    string? RunnerId,
    IReadOnlyList<string> Scopes);

public sealed record AgentAssignmentToken(string Token, AgentIdentity Identity);

public static class AgentIdentityContext
{
    public const string Key = "RegentAgent.AgentIdentity";
    public static AgentIdentity Required(IDictionary<object, object?> items) =>
        items.TryGetValue(Key, out var value) && value is AgentIdentity identity
            ? identity : throw new UnauthorizedAccessException("Agent identity is required.");
}
