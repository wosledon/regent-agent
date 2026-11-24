using System.Text.Json;
using RegentAgent.Contracts.Runtime;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class TaskToolHandler(TaskExecutionService tasks) : IAgentToolHandler
{
    public string ToolName => "task";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["tasks:execute"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        var taskId = AgentToolArguments.RequiredGuid(arguments, "taskId");
        if (context.Identity.TaskId != taskId) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
        return operation switch
        {
            "checkpoint" => await CheckpointAsync(context, arguments, taskId, cancellationToken),
            "complete" => await CompleteAsync(context, arguments, taskId, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported task tool operation.")
        };
    }

    private async Task<object> CheckpointAsync(AgentToolContext context, JsonElement arguments, Guid taskId,
        CancellationToken cancellationToken)
    {
        var completed = await tasks.CheckpointAsync(taskId, new TaskCheckpoint(
            AgentToolArguments.RequiredString(arguments, "leaseToken"),
            AgentToolArguments.RequiredString(arguments, "checkpointJson")), context.Identity.TenantId, cancellationToken);
        if (!completed) throw new InvalidOperationException("Task checkpoint was rejected by the lease boundary.");
        return new { completed = true };
    }

    private async Task<object> CompleteAsync(AgentToolContext context, JsonElement arguments, Guid taskId,
        CancellationToken cancellationToken)
    {
        var completed = await tasks.CompleteAsync(taskId, new TaskCompletion(
            AgentToolArguments.RequiredString(arguments, "leaseToken"),
            AgentToolArguments.RequiredString(arguments, "output"),
            AgentToolArguments.OptionalString(arguments, "status", "Done"),
            AgentToolArguments.RequiredStrings(arguments, "evidenceRefs")), context.Identity.TenantId, cancellationToken);
        if (!completed) throw new InvalidOperationException("Task completion was rejected by the lease boundary.");
        return new { completed = true };
    }
}
