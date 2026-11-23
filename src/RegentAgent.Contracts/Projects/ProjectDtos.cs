using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Projects;

public sealed record CreateProjectDto(string Name, string Owner, string Mission, int TargetDays, long BudgetMinor, string Currency);

public sealed record ProjectSummary(
    Guid Id,
    string Name,
    ProjectStage Stage,
    ProjectHealth Health,
    string Owner,
    DateTimeOffset TargetAt,
    long BudgetMinor,
    long SpentMinor,
    string Currency,
    Freshness Freshness);

public sealed record ProjectDetail(
    ProjectSummary Project,
    IReadOnlyList<ProjectMilestone> Milestones,
    IReadOnlyList<ProjectTaskSummary> Tasks);

public sealed record ProjectMilestone(
    Guid Id,
    string Name,
    DateTimeOffset TargetAt,
    string Status,
    string AcceptanceRule);

public sealed record ProjectTaskSummary(
    Guid Id,
    string Title,
    string TaskType,
    string Status,
    string Assignee,
    int Weight,
    long CostMinor,
    string Currency);
