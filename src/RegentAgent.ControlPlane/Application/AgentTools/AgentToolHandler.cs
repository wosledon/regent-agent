using System.Text.Json;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed record AgentToolContext(
    AgentIdentityModel Identity,
    string ActorName,
    string TaskGrant,
    string Taint,
    string ArgumentsJson);

public interface IAgentToolHandler
{
    string ToolName { get; }
    string Version { get; }
    IReadOnlyList<string> Capabilities { get; }
    Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments, CancellationToken cancellationToken);
}

internal static class AgentToolArguments
{
    public static string RequiredString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidOperationException($"Tool argument {name} is required.");
        return value.GetString()!;
    }

    public static string? OptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    public static string OptionalString(JsonElement arguments, string name, string fallback) =>
        OptionalString(arguments, name) ?? fallback;

    public static Guid RequiredGuid(JsonElement arguments, string name) =>
        Guid.TryParse(RequiredString(arguments, name), out var value) ? value
            : throw new InvalidOperationException($"Tool argument {name} must be a UUID.");

    public static IReadOnlyList<string> RequiredStrings(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Tool argument {name} is required.");
        return value.EnumerateArray().Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray();
    }

    public static IReadOnlyList<string> OptionalStrings(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
            : [];

    public static int OptionalInt(JsonElement arguments, string name, int fallback) =>
        arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    public static double OptionalDouble(JsonElement arguments, string name, double fallback) =>
        arguments.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : fallback;

    public static long OptionalLong(JsonElement arguments, string name, long fallback) =>
        arguments.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : fallback;
}
