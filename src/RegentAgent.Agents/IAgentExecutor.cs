using RegentAgent.Contracts.Runtime;

namespace RegentAgent.Agents;

public sealed record AgentResult(string Output, string Status, IReadOnlyList<string> EvidenceRefs);

public interface IAgentExecutor
{
    Task<AgentResult> ExecuteAsync(TaskClaim task, CancellationToken cancellationToken);
}
