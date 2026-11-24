namespace RegentAgent.AgentIdentity;

public static class AgentScopes
{
    public static readonly IReadOnlyList<string> Baseline =
    [
        "tasks:execute", "chat:write", "infer:employee", "memory.read", "memory.write",
        "workflow.capture", "improvement.propose"
    ];
}
