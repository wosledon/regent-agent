using System.Text.Json;
using RegentAgent.Contracts.Dashboard;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

internal static class Mappings
{
    public static DashboardCard ToCard(string id, string title, string summary, string? metric,
        Severity severity, Freshness freshness, DateTimeOffset dataAt, string href,
        string? titleKey = null, string? summaryKey = null, DashboardCardSummaryValues? summaryValues = null,
        DashboardMetricPresentation? metricPresentation = null) =>
        new(id, title, summary, metric, severity, freshness, dataAt, href)
        {
            TitleKey = titleKey,
            SummaryKey = summaryKey,
            SummaryValues = summaryValues,
            MetricPresentation = metricPresentation
        };

    public static ProjectSummary ToSummary(ProjectRecord project, Freshness freshness) => new(
        project.Id, project.Name, project.Stage, project.Health, project.Owner,
        project.TargetAt, project.BudgetMinor, project.SpentMinor, project.Currency, freshness);

    public static ProjectDetail ToDetail(ProjectRecord project, Freshness freshness) => new(
        ToSummary(project, freshness),
        project.Milestones.Select(x => new ProjectMilestone(x.Id, x.Name, x.TargetAt, x.Status, x.AcceptanceRule)).ToArray(),
        project.Tasks.Select(x => new ProjectTaskSummary(x.Id, x.Title, x.TaskType, x.Status,
            x.Assignee, x.Weight, x.CostMinor, x.Currency)).ToArray());

    public static EmployeeSummary ToSummary(EmployeeRecord employee, Freshness freshness) => new(
        employee.Id, employee.EmployeeNo, employee.DisplayName, employee.Position,
        employee.State, employee.StateReason, freshness);

    public static EmployeeWorkEntry ToWorkEntry(WorkEntryRecord entry)
    {
        var evidence = JsonSerializer.Deserialize<string[]>(entry.EvidenceJson) ?? [];
        return new EmployeeWorkEntry(entry.Id, entry.OccurredAt, entry.TaskId,
            entry.Title, entry.EventType, entry.Status, entry.NextAction, entry.CostMinor, entry.Currency, evidence);
    }

    public static ApprovalDto ToDto(ApprovalRecord approval) => new(
        approval.Id, approval.ActionType, approval.Title, approval.Impact,
        approval.Severity, approval.State, approval.Requester, approval.CreatedAt, approval.ExpiresAt,
        JsonSerializer.Deserialize<string[]>(approval.OptionsJson) ?? [],
        JsonSerializer.Deserialize<string[]>(approval.EvidenceJson) ?? [],
        approval.TargetEmployeeId, approval.TargetCandidateId, approval.TargetPositionId,
        approval.ConsumedAt, approval.ConsumedBy, approval.DecidedBy, approval.DecidedAt);
}
