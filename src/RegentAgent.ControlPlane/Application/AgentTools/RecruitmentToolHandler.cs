using System.Text.Json;
using RegentAgent.Contracts.Organization;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class RecruitmentToolHandler(OrganizationService organization) : IAgentToolHandler
{
    public string ToolName => "recruitment";
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["recruitment.execute"];

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "create_candidate" => await organization.CreateCandidateAsync(
                AgentToolArguments.RequiredGuid(arguments, "positionId"),
                AgentToolArguments.RequiredString(arguments, "displayName"),
                AgentToolArguments.RequiredString(arguments, "modelVersion"),
                AgentToolArguments.RequiredString(arguments, "jobDescription"), cancellationToken),
            "assess" => await organization.AssessCandidateAsync(AgentToolArguments.RequiredGuid(arguments, "candidateId"),
                new CandidateAssessmentDto(AgentToolArguments.RequiredGuid(arguments, "candidateId"),
                    AgentToolArguments.OptionalDecimal(arguments, "capabilityScore", 0),
                    AgentToolArguments.OptionalDecimal(arguments, "qualityScore", 0),
                    AgentToolArguments.OptionalDecimal(arguments, "costScore", 0),
                    AgentToolArguments.OptionalString(arguments, "evidenceJson", "{}")), cancellationToken),
            "reject" => await organization.RejectCandidateAsync(AgentToolArguments.RequiredGuid(arguments, "candidateId"),
                AgentToolArguments.RequiredString(arguments, "reason"), cancellationToken),
            "retry" => await organization.RetryCandidateAsync(AgentToolArguments.RequiredGuid(arguments, "candidateId"),
                cancellationToken),
            "interview" => await RecordInterviewAsync(context, arguments, cancellationToken),
            "fairness_audit" => await organization.RunFairnessAuditAsync(
                AgentToolArguments.RequiredGuid(arguments, "positionId"),
                AgentToolArguments.RequiredString(arguments, "period"),
                AgentToolArguments.OptionalInt(arguments, "sampleSize", 30),
                AgentToolArguments.OptionalDecimal(arguments, "selectionRateRatio", 1m), cancellationToken),
            _ => throw new InvalidOperationException("Unsupported recruitment tool operation.")
        };
    }

    private async Task<object> RecordInterviewAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var stage = JsonSerializer.Deserialize<InterviewStageDto>(arguments.GetProperty("stage").GetRawText())
            ?? throw new InvalidOperationException("Interview stage is required.");
        stage.TenantId = context.Identity.TenantId;
        return await organization.RecordInterviewStageAsync(stage, cancellationToken);
    }
}
