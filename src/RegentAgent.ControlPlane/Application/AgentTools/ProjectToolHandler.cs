using System.Text.Json;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class ProjectToolHandler(ProjectService projects, DeliveryService delivery) : IAgentToolHandler
{
    public string ToolName => "project";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities =>
        ["project.decide", "research.execute", "product.define", "tasks:execute"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "create" => await projects.CreateAsync(new CreateProjectDto(
                AgentToolArguments.RequiredString(arguments, "name"), context.ActorName,
                AgentToolArguments.OptionalString(arguments, "mission", string.Empty),
                AgentToolArguments.OptionalInt(arguments, "targetDays", 14),
                AgentToolArguments.OptionalLong(arguments, "budgetMinor", 0),
                AgentToolArguments.OptionalString(arguments, "currency", "CNY")), cancellationToken),
            "add_finding" => await AddFindingAsync(context, arguments, cancellationToken),
            "create_requirement" => await CreateRequirementAsync(context, arguments, cancellationToken),
            "claim_task" => await delivery.ClaimTaskAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "projectId"),
                AgentToolArguments.RequiredGuid(arguments, "taskId"), context.ActorName, cancellationToken),
            "submit_task" => await delivery.SubmitTaskAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "projectId"),
                AgentToolArguments.RequiredGuid(arguments, "taskId"), context.ActorName, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported project tool operation.")
        };
    }

    private async Task<object> AddFindingAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var sourceA = AgentToolArguments.RequiredString(arguments, "sourceA");
        var sourceB = AgentToolArguments.RequiredString(arguments, "sourceB");
        var finding = new ResearchFinding
        {
            Claim = AgentToolArguments.RequiredString(arguments, "claim"),
            Sources =
            [
                new EvidenceRef(sourceA, "source", sourceA),
                new EvidenceRef(sourceB, "source", sourceB)
            ],
            KeyClaim = arguments.TryGetProperty("keyClaim", out var keyClaim) && keyClaim.GetBoolean(),
            CounterEvidence = AgentToolArguments.OptionalString(arguments, "counterEvidence")
        };
        return await delivery.AddFindingAsync(context.Identity.TenantId,
            AgentToolArguments.RequiredGuid(arguments, "projectId"), finding, cancellationToken);
    }

    private async Task<object> CreateRequirementAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var requirement = new DeliveryRequirement
        {
            Title = AgentToolArguments.RequiredString(arguments, "title"),
            Problem = AgentToolArguments.RequiredString(arguments, "problem"),
            Scope = AgentToolArguments.RequiredString(arguments, "scope"),
            Priority = AgentToolArguments.OptionalString(arguments, "priority", "P1"),
            AcceptanceCriteria = AgentToolArguments.RequiredStrings(arguments, "acceptanceCriteria")
        };
        return await delivery.CreateRequirementAsync(context.Identity.TenantId,
            AgentToolArguments.RequiredGuid(arguments, "projectId"), requirement, cancellationToken);
    }
}
