using System.Text.Json;
using System.Text.Json.Serialization;
using RegentAgent.Contracts.Collaboration;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class CollaborationToolHandler(CollaborationCoordinationService coordination) : IAgentToolHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string ToolName => "collaboration";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["tasks:execute"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        var idempotencyKey = AgentToolArguments.OptionalString(arguments, "idempotencyKey");
        return operation switch
        {
            "delegate" => await coordination.CreateDelegationAsync(context.Identity, new CreateTaskDelegationRequest(
                AgentToolArguments.RequiredGuid(arguments, "taskId"),
                AgentToolArguments.RequiredGuid(arguments, "delegateEmployeeId"),
                AgentToolArguments.OptionalInt(arguments, "weight", 1),
                AgentToolArguments.RequiredString(arguments, "acceptanceCriteria"),
                AgentToolArguments.RequiredStrings(arguments, "evidenceRequirements"),
                AgentToolArguments.OptionalStrings(arguments, "requiredCapabilities"),
                AgentToolArguments.OptionalString(arguments, "reason", string.Empty)), idempotencyKey, cancellationToken),
            "decide_delegation" => await coordination.DecideDelegationAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"),
                Decision(arguments), idempotencyKey, cancellationToken),
            "cancel_delegation" => await coordination.CancelDelegationAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"),
                AgentToolArguments.RequiredString(arguments, "reason"), idempotencyKey, cancellationToken),
            "handoff" => await coordination.CreateHandoffAsync(context.Identity, new CreateHandoffRequest(
                AgentToolArguments.RequiredGuid(arguments, "taskId"),
                AgentToolArguments.RequiredGuid(arguments, "recipientEmployeeId"),
                AgentToolArguments.RequiredString(arguments, "leaseToken"),
                Deserialize<HandoffPackageDto>(arguments, "package")), idempotencyKey, cancellationToken),
            "decide_handoff" => await coordination.DecideHandoffAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"),
                Decision(arguments), idempotencyKey, cancellationToken),
            "help" => await coordination.CreateHelpAsync(context.Identity, new CreateHelpRequest(
                AgentToolArguments.RequiredGuid(arguments, "taskId"),
                AgentToolArguments.RequiredGuid(arguments, "helperEmployeeId"),
                AgentToolArguments.RequiredString(arguments, "question"),
                AgentToolArguments.RequiredStrings(arguments, "attemptedSteps"),
                AgentToolArguments.OptionalStrings(arguments, "evidenceRefs"),
                AgentToolArguments.RequiredString(arguments, "expectedAnswer"),
                Deadline(arguments)), idempotencyKey, cancellationToken),
            "respond_help" => await coordination.RespondHelpAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new HelpResponseRequest(
                    AgentToolArguments.RequiredString(arguments, "responseType"),
                    AgentToolArguments.OptionalString(arguments, "answer", string.Empty),
                    OptionalGuid(arguments, "redirectEmployeeId")), idempotencyKey, cancellationToken),
            "help_context" => await coordination.AddHelpContextAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new HelpContextRequest(
                    AgentToolArguments.RequiredString(arguments, "context"),
                    AgentToolArguments.OptionalStrings(arguments, "evidenceRefs")), idempotencyKey, cancellationToken),
            "resolve_help" => await coordination.ResolveHelpAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"),
                AgentToolArguments.RequiredString(arguments, "resolution"), idempotencyKey, cancellationToken),
            "interrupt" => await coordination.CreateInterruptAsync(context.Identity, new CreateInterruptRequest(
                AgentToolArguments.RequiredGuid(arguments, "taskId"),
                AgentToolArguments.RequiredString(arguments, "priority"),
                AgentToolArguments.RequiredString(arguments, "reason")), idempotencyKey, cancellationToken),
            "acknowledge_interrupt" => await coordination.AcknowledgeInterruptAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new InterruptCheckpointRequest(
                    AgentToolArguments.RequiredString(arguments, "leaseToken"),
                    AgentToolArguments.RequiredString(arguments, "checkpointJson")), idempotencyKey, cancellationToken),
            "resolve_interrupt" => await coordination.ResolveInterruptAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new CoordinationResolutionRequest(
                    AgentToolArguments.RequiredString(arguments, "resolution")), idempotencyKey, cancellationToken),
            "resume_interrupt" => await coordination.ResumeInterruptAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new InterruptResumeRequest(
                    AgentToolArguments.RequiredString(arguments, "leaseToken")), idempotencyKey, cancellationToken),
            "cancel_interrupt" => await coordination.CancelInterruptAsync(context.Identity,
                AgentToolArguments.RequiredGuid(arguments, "coordinationId"), new CoordinationCancelRequest(
                    AgentToolArguments.RequiredString(arguments, "reason")), idempotencyKey, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported collaboration tool operation.")
        };
    }

    private static CoordinationDecisionRequest Decision(JsonElement arguments) => new(
        AgentToolArguments.OptionalBool(arguments, "accepted", false),
        AgentToolArguments.OptionalString(arguments, "reason", string.Empty));

    private static DateTimeOffset Deadline(JsonElement arguments) =>
        DateTimeOffset.TryParse(AgentToolArguments.RequiredString(arguments, "deadline"), out var deadline)
            ? deadline : throw new InvalidOperationException("Tool argument deadline must be an ISO-8601 timestamp.");

    private static Guid? OptionalGuid(JsonElement arguments, string name) =>
        Guid.TryParse(AgentToolArguments.OptionalString(arguments, name), out var value) ? value : null;

    private static T Deserialize<T>(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Tool argument {name} is required.");
        return JsonSerializer.Deserialize<T>(value.GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException($"Tool argument {name} is invalid.");
    }
}
