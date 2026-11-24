using System.Text.Json;
using RegentAgent.Workflows;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class WorkflowToolHandler(WorkflowService workflows) : IAgentToolHandler
{
    public string ToolName => "workflow";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["workflow.capture"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "capture" => await workflows.CaptureAsync(context.Identity.TenantId.ToString(),
                AgentToolArguments.RequiredString(arguments, "name"),
                AgentToolArguments.RequiredStrings(arguments, "steps"), context.ActorName,
                AgentToolArguments.RequiredString(arguments, "scopePositionId"), cancellationToken),
            "nominate" => await NominateAsync(context, arguments, cancellationToken),
            "start" => await workflows.StartAsync(AgentToolArguments.RequiredString(arguments, "workflowId"),
                context.Identity.TenantId.ToString(), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported workflow tool operation.")
        };
    }

    private async Task<object> NominateAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var workflowId = AgentToolArguments.RequiredString(arguments, "workflowId");
        var source = await workflows.GetAsync(workflowId, context.Identity.TenantId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException("Workflow not found.");
        if (source.Author != context.ActorName) throw new UnauthorizedAccessException("Only the workflow author can nominate it.");
        return await workflows.NominateAsync(workflowId, context.Identity.TenantId.ToString(), cancellationToken);
    }
}
