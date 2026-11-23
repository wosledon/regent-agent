using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Organization;

public sealed record EmployeeSummary(
    Guid Id,
    string EmployeeNo,
    string DisplayName,
    string Position,
    EmployeeState State,
    string? StateReason,
    Freshness Freshness);

public sealed record EmployeeWorkEntry(
    Guid Id,
    DateTimeOffset OccurredAt,
    Guid? TaskId,
    string Title,
    string EventType,
    string Status,
    string? NextAction,
    long CostMinor,
    string Currency,
    IReadOnlyList<string> EvidenceRefs);

public sealed record EmployeeTimeline(
    EmployeeSummary Employee,
    IReadOnlyList<EmployeeWorkEntry> Entries,
    DateTimeOffset GeneratedAt);
