using System.Text.Json;
using RegentAgent.Memory;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class MemoryToolHandler(MemoryService memory) : IAgentToolHandler
{
    public string ToolName => "memory";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["memory.read", "memory.write"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "capture" => await memory.CaptureAsync(context.Identity.TenantId.ToString(),
                AgentToolArguments.OptionalString(arguments, "scope", "personal"),
                AgentToolArguments.RequiredString(arguments, "content"),
                AgentToolArguments.RequiredString(arguments, "sourceRef"),
                AgentToolArguments.OptionalDouble(arguments, "confidence", 0.5),
                AgentToolArguments.OptionalString(arguments, "taint", "untrusted"), context.ActorName,
                cancellationToken, AgentToolArguments.OptionalString(arguments, "scope", "personal") != "personal",
                context.Identity.EmployeeId.ToString(),
                authorEmploymentPeriodId: context.Identity.CredentialEpoch.ToString()),
            "search" => await SearchAsync(context, arguments, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported memory tool operation.")
        };
    }

    private async Task<object> SearchAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var scope = AgentToolArguments.OptionalString(arguments, "scope");
        var results = await memory.SearchAsync(context.Identity.TenantId.ToString(),
            AgentToolArguments.OptionalString(arguments, "query"), context.ActorName, cancellationToken,
            context.Identity.EmployeeId.ToString(),
            viewerEmploymentPeriodId: context.Identity.CredentialEpoch.ToString());
        return scope is null ? results : results.Where(x => x.Scope == scope).ToArray();
    }
}
