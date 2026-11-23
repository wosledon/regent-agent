using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Agents;
using RegentAgent.Storage;

namespace RegentAgent.Agents;

public sealed class AgentRuntimeService(JsonDocumentStore<AgentContextSnapshot> contexts, TimeProvider clock)
{
    public async Task<AgentContextSnapshot> BuildContextAsync(Guid taskId, string systemPrefix, string tenantMemory,
        string taskGrantJson, IReadOnlyList<string> toolResultRefs, string checkpointJson, bool compressed,
        CancellationToken cancellationToken)
    {
        var snapshot = new AgentContextSnapshot
        {
            TaskId = taskId, SystemPrefix = systemPrefix, TenantMemory = tenantMemory,
            TaskGrantJson = taskGrantJson, ToolResultRefs = toolResultRefs,
            CheckpointJson = checkpointJson, Compressed = compressed,
            PrefixHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(systemPrefix))).ToLowerInvariant()
        };
        await contexts.PutAsync(taskId.ToString(), snapshot, cancellationToken);
        return snapshot;
    }

    public async Task<ToolLoopResult> RunToolLoopAsync(ToolLoopRequest request, CancellationToken cancellationToken)
    {
        if (request.MaxSteps is < 1 or > 32) throw new InvalidOperationException("Tool loop bound must be between 1 and 32.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        var duplicates = 0;
        var unauthorized = 0;
        decimal cost = 0;
        var executed = 0;
        foreach (var step in request.Steps.Take(request.MaxSteps))
        {
            var key = $"{step.ToolName}:{step.Capability}";
            if (!seen.Add(key)) { duplicates++; continue; }
            if (!step.Authorized) { unauthorized++; continue; }
            executed++;
            cost += step.Cost;
            results.Add(step.Result);
        }
        var checkpoint = JsonSerializer.Serialize(new { request.TaskId, executed, duplicates, unauthorized, cost, at = clock.GetUtcNow() });
        var context = await contexts.GetAsync(request.TaskId.ToString(), cancellationToken);
        if (context is not null)
        {
            context.CheckpointJson = checkpoint;
            context.ToolResultRefs = results.ToArray();
            await contexts.PutAsync(request.TaskId.ToString(), context, cancellationToken);
        }
        return new ToolLoopResult
        {
            Completed = executed > 0 && unauthorized == 0, ExecutedSteps = executed,
            DuplicateSteps = duplicates, UnauthorizedSteps = unauthorized, TotalCost = cost,
            ReviewRequired = cost >= request.CostWarningThreshold || duplicates > 0 || unauthorized > 0,
            CheckpointJson = checkpoint, Results = results
        };
    }
}
