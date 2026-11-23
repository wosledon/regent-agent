namespace RegentAgent.Contracts.Projects;

public sealed record EvidenceRef(string Id, string Kind, string Location);

public sealed class ResearchFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Claim { get; set; } = string.Empty;
    public IReadOnlyList<EvidenceRef> Sources { get; set; } = [];
    public bool KeyClaim { get; set; }
    public string? CounterEvidence { get; set; }
}

public sealed class ProductBrief
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public IReadOnlyList<string> MvpScope { get; set; } = [];
    public IReadOnlyDictionary<string, string> SuccessMetrics { get; set; } = new Dictionary<string, string>();
    public IReadOnlyList<Guid> FindingIds { get; set; } = [];
    public int Version { get; set; } = 1;
}

public sealed class GoNoGoDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid BriefId { get; set; }
    public string Decision { get; set; } = "NoGo";
    public string Rationale { get; set; } = string.Empty;
    public IReadOnlyList<Guid> EvidenceIds { get; set; } = [];
    public int Reround { get; set; }
    public bool Forced { get; set; }
    public string Decider { get; set; } = string.Empty;
}

public sealed class DeliveryRequirement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string Priority { get; set; } = "P1";
    public string Status { get; set; } = "Draft";
    public IReadOnlyList<string> AcceptanceCriteria { get; set; } = [];
    public int Version { get; set; } = 1;
    public int TotalWeight { get; set; }
}

public sealed class DecompositionPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequirementId { get; set; }
    public int RequirementVersion { get; set; }
    public IReadOnlyList<DeliveryTask> Tasks { get; set; } = [];
    public IReadOnlyList<string> ValidationErrors { get; set; } = [];
    public string Status { get; set; } = "Draft";
}

public sealed class DeliveryTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequirementId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TaskType { get; set; } = "engineering.implement";
    public string RequiredCapabilitiesJson { get; set; } = "[]";
    public IReadOnlyList<Guid> DependsOn { get; set; } = [];
    public int Weight { get; set; } = 1;
    public string Status { get; set; } = "Backlog";
    public string Author { get; set; } = string.Empty;
    public string? Assignee { get; set; }
    public string? Validator { get; set; }
    public IReadOnlyList<string> AcceptanceCriteria { get; set; } = [];
}

public sealed class Defect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequirementId { get; set; }
    public Guid? TaskId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "P2";
    public string Status { get; set; } = "New";
    public string ReproductionSteps { get; set; } = string.Empty;
    public string ReportedBy { get; set; } = string.Empty;
    public string? Fixer { get; set; }
    public string? Retester { get; set; }
    public IReadOnlyList<EvidenceRef> Evidence { get; set; } = [];
}

public sealed class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = "Task";
    public string Reviewer { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Verdict { get; set; } = "Pending";
    public IReadOnlyList<string> Findings { get; set; } = [];
    public IReadOnlyList<EvidenceRef> Evidence { get; set; } = [];
}

public sealed class AcceptanceReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequirementId { get; set; }
    public string Reviewer { get; set; } = string.Empty;
    public int FunctionalScore { get; set; }
    public int UsabilityScore { get; set; }
    public int DesignScore { get; set; }
    public int AccessibilityScore { get; set; }
    public IReadOnlyList<EvidenceRef> Evidence { get; set; } = [];
    public bool Passed => FunctionalScore >= 80 && UsabilityScore >= 80 && DesignScore >= 75 && AccessibilityScore >= 70;
}

public sealed class ReleaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string ArtifactRef { get; set; } = string.Empty;
    public string Status { get; set; } = "Released";
    public string? RolledBackTo { get; set; }
    public DateTimeOffset ReleasedAt { get; set; }
}

public sealed class Retrospective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string WhatWorked { get; set; } = string.Empty;
    public string WhatFailed { get; set; } = string.Empty;
    public IReadOnlyList<string> Lessons { get; set; } = [];
}

public sealed class DoneEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkItemId { get; set; }
    public string WorkItemType { get; set; } = "Task";
    public IReadOnlyDictionary<string, bool> CriteriaResults { get; set; } = new Dictionary<string, bool>();
    public IReadOnlyList<EvidenceRef> Artifacts { get; set; } = [];
    public IReadOnlyList<string> TestResults { get; set; } = [];
    public IReadOnlyList<string> SecurityChecks { get; set; } = [];
    public int OpenP0P1Defects { get; set; }
    public string Validator { get; set; } = string.Empty;
    public DateTimeOffset ValidatedAt { get; set; }
}

public sealed class ProjectDeliveryState
{
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public List<ResearchFinding> Findings { get; set; } = [];
    public List<ProductBrief> Briefs { get; set; } = [];
    public List<GoNoGoDecision> Decisions { get; set; } = [];
    public List<DeliveryRequirement> Requirements { get; set; } = [];
    public List<DecompositionPlan> Plans { get; set; } = [];
    public List<DeliveryTask> Tasks { get; set; } = [];
    public List<Defect> Defects { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    public List<AcceptanceReport> AcceptanceReports { get; set; } = [];
    public List<ReleaseRecord> Releases { get; set; } = [];
    public List<Retrospective> Retrospectives { get; set; } = [];
    public List<DoneEvidence> Evidence { get; set; } = [];
}
