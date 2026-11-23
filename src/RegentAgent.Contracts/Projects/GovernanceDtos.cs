namespace RegentAgent.Contracts.Projects;

public sealed class TaskTypeDefinition
{
    public string TaskType { get; set; } = string.Empty;
    public IReadOnlyList<string> RequiredCapabilities { get; set; } = [];
    public string ToolProfile { get; set; } = "Primary";
    public string DoneTemplate { get; set; } = "{}";
    public int Version { get; set; } = 1;
}

public sealed class CapabilityAssessment
{
    public Guid EmployeeId { get; set; }
    public string Dimension { get; set; } = string.Empty;
    public decimal ModelScore { get; set; }
    public decimal HistoryScore { get; set; }
    public decimal ToolFit { get; set; }
    public decimal ContextFit { get; set; }
    public decimal EffectiveScore { get; set; }
    public decimal Confidence { get; set; } = 0.8m;
}

public sealed record ClaimScoreDto(Guid EmployeeId, decimal Score, bool Eligible, string Reason);

public sealed class WorkLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TaskId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public int TaskWeight { get; set; }
    public string RoleRelation { get; set; } = "Primary";
    public decimal QualityFactor { get; set; } = 1m;
    public decimal CapabilityFitFactor { get; set; } = 1m;
    public decimal WeightedScore { get; set; }
    public decimal Cost { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
}

public sealed class ReviewTaskRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SubjectId { get; set; }
    public string Author { get; set; } = string.Empty;
    public int RequiredQuorum { get; set; } = 2;
    public IReadOnlyList<string> Findings { get; set; } = [];
    public IReadOnlyList<string> Approvers { get; set; } = [];
    public string Status { get; set; } = "Open";
}

public sealed class DebateTaskRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Question { get; set; } = string.Empty;
    public IReadOnlyList<string> Options { get; set; } = [];
    public DateTimeOffset Deadline { get; set; }
    public string? Decision { get; set; }
    public string Status { get; set; } = "Open";
}

public sealed class AdjudicationTaskRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DebateId { get; set; }
    public string Adjudicator { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string DissentRefsJson { get; set; } = "[]";
}

public sealed class TaskGovernanceState
{
    public List<TaskTypeDefinition> TaskTypes { get; set; } = [];
    public List<CapabilityAssessment> Capabilities { get; set; } = [];
    public List<WorkLedgerEntry> Ledger { get; set; } = [];
    public List<ReviewTaskRecord> Reviews { get; set; } = [];
    public List<DebateTaskRecord> Debates { get; set; } = [];
    public List<AdjudicationTaskRecord> Adjudications { get; set; } = [];
}
