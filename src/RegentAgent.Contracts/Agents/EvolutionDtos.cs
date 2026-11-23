namespace RegentAgent.Contracts.Agents;

public sealed class PromptBundle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SkillDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public Guid PromptBundleId { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public string Status { get; set; } = "Draft";
}

public sealed class ToolDisclosurePolicy
{
    public string ToolName { get; set; } = string.Empty;
    public string Level { get; set; } = "Primary";
    public IReadOnlyList<string> RequiredCapabilities { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

public sealed record ToolDisclosureRequest(IReadOnlyList<string> Capabilities, bool RestrictedActivated);
public sealed record DisclosedTool(string Name, string Level, string Summary);
public sealed record ToolDisclosureResult(IReadOnlyList<DisclosedTool> Tools, bool PermissionExpanded);

public sealed class AgentEvolutionState
{
    public List<PromptBundle> Prompts { get; set; } = [];
    public List<SkillDefinition> Skills { get; set; } = [];
    public List<ToolDisclosurePolicy> ToolPolicies { get; set; } = [];
}
