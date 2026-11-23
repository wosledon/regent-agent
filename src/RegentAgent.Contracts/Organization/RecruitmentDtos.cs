namespace RegentAgent.Contracts.Organization;

public sealed class InterviewStageDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CandidateId { get; set; }
    public string Stage { get; set; } = "Screening";
    public string Status { get; set; } = "Completed";
    public decimal Score { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CandidateAppealDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CandidateId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string? Resolution { get; set; }
}

public sealed class FairnessAuditDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PositionId { get; set; }
    public string Period { get; set; } = string.Empty;
    public int SampleSize { get; set; }
    public decimal SelectionRateRatio { get; set; }
    public string Status { get; set; } = "Passed";
    public string DetailsJson { get; set; } = "{}";
}
