using System.Text.Json;
using RegentAgent.Evaluation;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class EvaluationToolHandler(EvaluationService evaluation, EvaluationQaService qa) : IAgentToolHandler
{
    public string ToolName => "evaluation";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["evaluation.execute", "evaluation.judge"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "run" => await evaluation.RunAsync(AgentToolArguments.RequiredString(arguments, "suiteId"),
                AgentToolArguments.RequiredString(arguments, "candidateEmployeeId"),
                context.Identity.TenantId.ToString(), AgentToolArguments.OptionalInt(arguments, "seed", 42), cancellationToken),
            "qa_gate" => await qa.EvaluateGateAsync(context.Identity.TenantId,
                AgentToolArguments.RequiredString(arguments, "suiteId"),
                AgentToolArguments.RequiredGuid(arguments, "candidateEmployeeId"),
                AgentToolArguments.OptionalInt(arguments, "repeats", 3),
                AgentToolArguments.OptionalInt(arguments, "sampleCount", 100),
                AgentToolArguments.OptionalInt(arguments, "successes", 0),
                AgentToolArguments.OptionalInt(arguments, "baselineSuccesses", 0),
                AgentToolArguments.OptionalInt(arguments, "discordantA", 0),
                AgentToolArguments.OptionalInt(arguments, "discordantB", 0),
                AgentToolArguments.OptionalBool(arguments, "calibrationPassed", false), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported evaluation tool operation.")
        };
    }
}
