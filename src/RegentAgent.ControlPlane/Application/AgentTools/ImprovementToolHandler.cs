using System.Text.Json;
using RegentAgent.Performance;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class ImprovementToolHandler(ImprovementService improvements) : IAgentToolHandler
{
    public string ToolName => "improvement";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["improvement.propose"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        if (operation != "propose") throw new InvalidOperationException("Unsupported improvement tool operation.");
        return await improvements.ProposeAsync(context.Identity.TenantId.ToString(),
            context.Identity.EmployeeId.ToString(), AgentToolArguments.RequiredString(arguments, "category"),
            AgentToolArguments.RequiredString(arguments, "summary"),
            AgentToolArguments.RequiredString(arguments, "expectedBenefit"), cancellationToken);
    }
}
