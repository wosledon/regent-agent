using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Organization;

public sealed record TenantDto(Guid Id, string Name, string CompanyName, string Status);
public sealed record CharterDto(string Mission, IReadOnlyList<string> Constraints, long MonthlyBudgetMinor, string Currency, IReadOnlyDictionary<string, string> Governance, int Version);
public sealed record PositionDto(Guid Id, string Name, string Responsibilities, IReadOnlyList<string> Capabilities, long BudgetMinor, Guid? ParentPositionId, string Status, int Version);
public sealed record EmploymentPeriodDto(Guid Id, Guid EmployeeId, Guid PositionId, string Kind, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);
public sealed record OrgDelegationDto(Guid Id, Guid DelegateEmployeeId, Guid SubtreePositionId, IReadOnlyList<string> Actions, long BudgetLimitMinor, DateTimeOffset ExpiresAt, string Status);
public sealed record CandidateDto(Guid Id, Guid PositionId, string DisplayName, string ModelVersion, string Status, string AssessmentJson, string FairnessJson);
public sealed record CandidateAssessmentDto(Guid CandidateId, decimal CapabilityScore, decimal QualityScore, decimal CostScore, string EvidenceJson);
