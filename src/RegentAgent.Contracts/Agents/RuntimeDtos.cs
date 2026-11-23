namespace RegentAgent.Contracts.Agents;

public sealed class AgentContextSnapshot
{
    public Guid TaskId { get; set; }
    public string SystemPrefix { get; set; } = string.Empty;
    public string TenantMemory { get; set; } = string.Empty;
    public string TaskGrantJson { get; set; } = "{}";
    public IReadOnlyList<string> ToolResultRefs { get; set; } = [];
    public string CheckpointJson { get; set; } = "{}";
    public string PrefixHash { get; set; } = string.Empty;
    public bool Compressed { get; set; }
}

public sealed class ToolLoopStep
{
    public string ToolName { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public bool Authorized { get; set; }
    public string Result { get; set; } = string.Empty;
    public decimal Cost { get; set; }
}

public sealed class ToolLoopRequest
{
    public Guid TaskId { get; set; }
    public int MaxSteps { get; set; } = 8;
    public decimal CostWarningThreshold { get; set; } = 10m;
    public IReadOnlyList<ToolLoopStep> Steps { get; set; } = [];
}

public sealed class ToolLoopResult
{
    public bool Completed { get; set; }
    public int ExecutedSteps { get; set; }
    public int DuplicateSteps { get; set; }
    public int UnauthorizedSteps { get; set; }
    public decimal TotalCost { get; set; }
    public bool ReviewRequired { get; set; }
    public string CheckpointJson { get; set; } = "{}";
    public IReadOnlyList<string> Results { get; set; } = [];
}
