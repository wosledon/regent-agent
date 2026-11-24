using System.Text.Json;
using RegentAgent.Performance;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class PerformanceToolHandler(PerformanceService performance,
    PerformanceActionService actions, ImprovementService improvements) : IAgentToolHandler
{
    public string ToolName => "performance";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["performance.review", "performance.action"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "review" => await performance.ReviewAsync(AgentToolArguments.RequiredString(arguments, "employeeId"),
                context.Identity.TenantId.ToString(), AgentToolArguments.RequiredString(arguments, "conclusion"),
                AgentToolArguments.OptionalString(arguments, "evidenceJson", "{}"), cancellationToken),
            "action" => await PerformActionAsync(context, arguments, cancellationToken),
            "improvement_review" => await improvements.ReviewAsync(AgentToolArguments.RequiredString(arguments, "proposalId"),
                context.Identity.TenantId.ToString(), AgentToolArguments.OptionalBool(arguments, "approved", false),
                context.ActorName, AgentToolArguments.RequiredString(arguments, "note"), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported performance tool operation.")
        };
    }

    private async Task<object> PerformActionAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var actionType = AgentToolArguments.RequiredString(arguments, "actionType");
        if (actionType.Equals("Terminate", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Termination remains a human Boss control-plane decision.");
        return await actions.PerformAsync(context.Identity.TenantId.ToString(),
            AgentToolArguments.RequiredString(arguments, "employeeId"), actionType,
            AgentToolArguments.OptionalString(arguments, "approvalId"),
            AgentToolArguments.RequiredString(arguments, "reason"), cancellationToken);
    }
}
