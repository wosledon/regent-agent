using System.Text.Json;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class TaskGovernanceToolHandler(TaskGovernanceService governance) : IAgentToolHandler
{
    public string ToolName => "task_governance";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities =>
        ["tasks:execute", "acceptance.review", "technical.review", "project.decide"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "record_work" => await RecordWorkAsync(context, arguments, cancellationToken),
            "create_review" => await CreateReviewAsync(context, arguments, cancellationToken),
            "approve_review" => await governance.ApproveReviewAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "reviewId"), context.ActorName, cancellationToken),
            "create_debate" => await CreateDebateAsync(context, arguments, cancellationToken),
            "decide_debate" => await governance.DecideDebateAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredGuid(arguments, "debateId"),
                AgentToolArguments.RequiredString(arguments, "decision"), cancellationToken),
            "adjudicate" => await AdjudicateAsync(context, arguments, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported task governance tool operation.")
        };
    }

    private async Task<object> RecordWorkAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var entry = JsonSerializer.Deserialize<WorkLedgerEntry>(arguments.GetProperty("entry").GetRawText())
            ?? throw new InvalidOperationException("Work ledger entry is required.");
        entry.TenantId = context.Identity.TenantId;
        entry.EmployeeId = context.Identity.EmployeeId;
        entry.TaskId = context.Identity.TaskId ?? throw new InvalidOperationException("Task-scoped identity is required.");
        return await governance.RecordWorkAsync(context.Identity.TenantId, entry, cancellationToken);
    }

    private async Task<object> CreateReviewAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var review = JsonSerializer.Deserialize<ReviewTaskRecord>(arguments.GetProperty("review").GetRawText())
            ?? throw new InvalidOperationException("Review is required.");
        review.TenantId = context.Identity.TenantId;
        review.Author = context.ActorName;
        return await governance.CreateReviewAsync(context.Identity.TenantId, review, cancellationToken);
    }

    private async Task<object> CreateDebateAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var debate = JsonSerializer.Deserialize<DebateTaskRecord>(arguments.GetProperty("debate").GetRawText())
            ?? throw new InvalidOperationException("Debate is required.");
        debate.TenantId = context.Identity.TenantId;
        return await governance.CreateDebateAsync(context.Identity.TenantId, debate, cancellationToken);
    }

    private async Task<object> AdjudicateAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var adjudication = JsonSerializer.Deserialize<AdjudicationTaskRecord>(arguments.GetProperty("adjudication").GetRawText())
            ?? throw new InvalidOperationException("Adjudication is required.");
        adjudication.TenantId = context.Identity.TenantId;
        adjudication.Adjudicator = context.ActorName;
        return await governance.AdjudicateAsync(context.Identity.TenantId, adjudication, cancellationToken);
    }
}
