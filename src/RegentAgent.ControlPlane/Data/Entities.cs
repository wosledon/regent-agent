using RegentAgent.Contracts.Enums;

namespace RegentAgent.ControlPlane.Data;

public sealed class TenantRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "Regent Demo";
    public string CompanyName { get; set; } = "Regent Autonomous Company";
    public string Status { get; set; } = "Active";
}

public sealed class UserRecord
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class MembershipRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "Owner";
}

public sealed class CharterRecord
{
    public Guid TenantId { get; set; }
    public string Mission { get; set; } = string.Empty;
    public string ConstraintsJson { get; set; } = "[]";
    public long MonthlyBudgetMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public string GovernanceJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class PositionRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public string CapabilitiesJson { get; set; } = "[]";
    public long BudgetMinor { get; set; }
    public Guid? ParentPositionId { get; set; }
    public string Status { get; set; } = "Open";
    public int Version { get; set; } = 1;
}

public sealed class EmploymentPeriodRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid PositionId { get; set; }
    public string Kind { get; set; } = "Primary";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

public sealed class OrgDelegationRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid DelegatorEmployeeId { get; set; }
    public Guid DelegateEmployeeId { get; set; }
    public Guid SubtreePositionId { get; set; }
    public string ActionsJson { get; set; } = "[]";
    public long BudgetLimitMinor { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class CandidateRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PositionId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ModelVersion { get; set; } = string.Empty;
    public string JobDescription { get; set; } = string.Empty;
    public string Status { get; set; } = "Screening";
    public string AssessmentJson { get; set; } = "{}";
    public string FairnessJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class InterviewStageRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CandidateId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CandidateAppealRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CandidateId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Resolution { get; set; }
}

public sealed class FairnessAuditRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PositionId { get; set; }
    public string Period { get; set; } = string.Empty;
    public int SampleSize { get; set; }
    public decimal SelectionRateRatio { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
}

public sealed class EmployeeRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public EmployeeState State { get; set; } = EmployeeState.Active;
    public string? StateReason { get; set; }
}

public sealed class ProjectRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Mission { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public ProjectStage Stage { get; set; }
    public ProjectHealth Health { get; set; }
    public DateTimeOffset TargetAt { get; set; }
    public long BudgetMinor { get; set; }
    public long SpentMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public DateTimeOffset UpdatedAt { get; set; }
    public List<MilestoneRecord> Milestones { get; set; } = [];
    public List<TaskRecord> Tasks { get; set; } = [];
}

public sealed class MilestoneRecord
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset TargetAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string AcceptanceRule { get; set; } = string.Empty;
}

public sealed class TaskRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Assignee { get; set; } = string.Empty;
    public int Weight { get; set; }
    public long CostMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public string? LeaseOwner { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public int Attempt { get; set; }
    public string? CheckpointJson { get; set; }
}

public sealed class WorkEntryRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? TaskId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? NextAction { get; set; }
    public long CostMinor { get; set; }
    public string Currency { get; set; } = "CNY";
    public string EvidenceJson { get; set; } = "[]";
}

public sealed class ApprovalRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public ApprovalState State { get; set; } = ApprovalState.Pending;
    public string Requester { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public string EvidenceJson { get; set; } = "[]";
}

public sealed class AuditRecord
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public long Seq { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = "null";
    public string Status { get; set; } = "Completed";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class OutboxRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class InboxRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "Processed";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SchemaVersionRecord
{
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset AppliedAt { get; set; }
}

public sealed class AuditCheckpointRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public long ThroughSeq { get; set; }
    public string MerkleRoot { get; set; } = string.Empty;
    public string Profile { get; set; } = "standard";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ArtifactRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TaskId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Length { get; set; }
    public string UploadState { get; set; } = "PendingUpload";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RecipientId { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
    public DateTimeOffset CreatedAt { get; set; }
}
