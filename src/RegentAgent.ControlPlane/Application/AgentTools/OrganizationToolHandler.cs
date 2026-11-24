using System.Text.Json;
using RegentAgent.Contracts.Organization;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class OrganizationToolHandler(OrganizationService organization) : IAgentToolHandler
{
    public string ToolName => "organization";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["org.design"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        if (operation != "create_position") throw new InvalidOperationException("Unsupported organization tool operation.");
        var parent = AgentToolArguments.OptionalString(arguments, "parentPositionId");
        return await organization.CreatePositionAsync(new PositionDto(Guid.NewGuid(),
            AgentToolArguments.RequiredString(arguments, "name"),
            AgentToolArguments.RequiredString(arguments, "responsibilities"),
            AgentToolArguments.RequiredStrings(arguments, "capabilities"),
            AgentToolArguments.OptionalLong(arguments, "budgetMinor", 0),
            Guid.TryParse(parent, out var parentId) ? parentId : null, "Open", 1), cancellationToken);
    }
}
