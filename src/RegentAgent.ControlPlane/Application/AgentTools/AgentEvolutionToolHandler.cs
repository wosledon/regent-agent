using System.Text.Json;
using RegentAgent.Agents;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class AgentEvolutionToolHandler(AgentEvolutionService evolution) : IAgentToolHandler
{
    public string ToolName => "agent_evolution";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["agent.evolve"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "create_prompt" => await evolution.CreatePromptAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredString(arguments, "name"),
                AgentToolArguments.RequiredString(arguments, "content"),
                AgentToolArguments.OptionalString(arguments, "kind", "Operations"), cancellationToken),
            "create_skill" => await evolution.CreateSkillAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredString(arguments, "name"),
                AgentToolArguments.RequiredGuid(arguments, "promptBundleId"),
                AgentToolArguments.OptionalString(arguments, "configJson", "{}"), cancellationToken),
            "activate_prompt" => await evolution.ActivatePromptAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "id"), cancellationToken),
            "activate_skill" => await evolution.ActivateSkillAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "id"), cancellationToken),
            "onboarding" => await evolution.CreateOnboardingBaselineAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "employeeId"),
                AgentToolArguments.RequiredGuid(arguments, "promptBundleId"), cancellationToken),
            "publish_asset" => await evolution.PublishAssetAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredString(arguments, "assetType"),
                AgentToolArguments.RequiredGuid(arguments, "assetId"), context.ActorName, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported Agent evolution tool operation.")
        };
    }
}
