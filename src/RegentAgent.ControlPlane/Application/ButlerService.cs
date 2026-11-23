using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Operations;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class ButlerService(RegentDbContext db,
    JsonDocumentStore<ButlerReport> reports,
    JsonDocumentStore<ReportSchedule> schedules,
    TimeProvider clock, GovernanceService? governance = null)
{
    public async Task<ButlerAnswer> AskAsync(Guid tenantId, string question, CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotAsync(tenantId, cancellationToken);
        var intent = Classify(question);
        var answer = intent switch
        {
            "budget" => string.Join("; ", snapshot.Projects.GroupBy(x => x.Currency)
                .Select(x => $"{x.Key}: spent {x.Sum(p => p.SpentMinor)} / budget {x.Sum(p => p.BudgetMinor)}")),
            "organization" => $"{snapshot.EmployeeCount} employees: {string.Join(", ", snapshot.EmployeeStates.Select(x => $"{x.Key}={x.Value}"))}.",
            "projects" => string.Join("; ", snapshot.Projects.Select(x => $"{x.Name}: {x.Health}, {x.Stage}")),
            "approvals" => $"{snapshot.PendingApprovals} pending approvals. Timeout defaults to rejection.",
            "hiring" => $"{snapshot.Candidates} candidates in the recruitment pipeline.",
            "security" => $"{snapshot.OpenIncidents} open security incidents.",
            _ => $"{snapshot.ProjectCount} projects, {snapshot.EmployeeCount} employees, {snapshot.PendingApprovals} pending approvals."
        };
        return new ButlerAnswer(answer, ["dashboard:summary", $"tenant:{tenantId}"], intent);
    }

    public async Task<IReadOnlyList<ButlerReport>> ListReportsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EnsureSchedulesAsync(tenantId, cancellationToken);
        return (await reports.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt).ToArray();
    }

    public async Task<IReadOnlyList<ReportSchedule>> ListSchedulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EnsureSchedulesAsync(tenantId, cancellationToken);
        return (await schedules.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .GroupBy(x => x.Kind, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToArray();
    }

    public async Task<ReportSchedule> SaveScheduleAsync(Guid tenantId, CreateReportScheduleRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind is not ("Daily" or "Weekly" or "Budget" or "Incident" or "Custom"))
            throw new InvalidOperationException("Invalid report kind.");
        if (request.Frequency is not ("Daily" or "Weekly" or "Manual")) throw new InvalidOperationException("Invalid report frequency.");
        var key = ScheduleKey(tenantId, request.Kind);
        var schedule = await schedules.GetAsync(key, cancellationToken) ?? new ReportSchedule { TenantId = tenantId, Kind = request.Kind };
        schedule.Frequency = request.Frequency;
        schedule.Enabled = request.Enabled;
        schedule.NextRunAt = NextRun(request.Frequency, clock.GetUtcNow());
        await schedules.PutAsync(key, schedule, cancellationToken);
        return schedule;
    }

    public async Task<ButlerReport> GenerateReportAsync(Guid tenantId, string kind, CancellationToken cancellationToken)
    {
        var snapshot = await SnapshotAsync(tenantId, cancellationToken);
        var title = kind switch
        {
            "Daily" => "Daily company report",
            "Weekly" => "Weekly company review",
            "Budget" => "Budget and usage report",
            "Incident" => "Security incident report",
            _ => "Butler report"
        };
        var summary = kind switch
        {
            "Budget" => string.Join("; ", snapshot.Projects.GroupBy(x => x.Currency)
                .Select(x => $"{x.Key}: {x.Sum(p => p.SpentMinor)} / {x.Sum(p => p.BudgetMinor)}")),
            "Incident" => $"{snapshot.OpenIncidents} open security incidents.",
            _ => $"{snapshot.ProjectCount} projects, {snapshot.EmployeeCount} employees, {snapshot.PendingApprovals} pending approvals."
        };
        var report = new ButlerReport
        {
            TenantId = tenantId, Kind = kind, Title = title, Summary = summary,
            DetailsJson = JsonSerializer.Serialize(snapshot), EvidenceRefs = ["dashboard:summary", "audit:latest"],
            CreatedAt = clock.GetUtcNow()
        };
        await reports.PutAsync(report.Id.ToString(), report, cancellationToken);
        return report;
    }

    public async Task<ButlerReport> RetryReportAsync(Guid tenantId, Guid reportId, CancellationToken cancellationToken)
    {
        var report = await reports.GetAsync(reportId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException("Report not found.");
        if (report.TenantId != tenantId) throw new UnauthorizedAccessException("Report belongs to another tenant.");
        return await GenerateReportAsync(tenantId, report.Kind, cancellationToken);
    }

    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = (await schedules.GetAllAsync(cancellationToken)).Where(x => x.Enabled && x.NextRunAt <= now).ToArray();
        foreach (var schedule in due)
        {
            try
            {
                await GenerateReportAsync(schedule.TenantId, schedule.Kind, cancellationToken);
                schedule.LastRunAt = now;
                schedule.LastError = null;
            }
            catch (Exception exception)
            {
                schedule.LastError = exception.Message;
            }
            schedule.NextRunAt = NextRun(schedule.Frequency, now);
            await schedules.PutAsync(ScheduleKey(schedule.TenantId, schedule.Kind), schedule, cancellationToken);
        }
    }

    private async Task EnsureSchedulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        foreach (var kind in new[] { "Daily", "Weekly" })
        {
            var key = ScheduleKey(tenantId, kind);
            if (await schedules.GetAsync(key, cancellationToken) is not null) continue;
            var schedule = new ReportSchedule
            {
                TenantId = tenantId, Kind = kind, Frequency = kind,
                NextRunAt = NextRun(kind, clock.GetUtcNow())
            };
            await schedules.PutAsync(key, schedule, cancellationToken);
        }
    }

    private static string ScheduleKey(Guid tenantId, string kind) => $"{tenantId:N}:{kind.ToLowerInvariant()}";

    private async Task<ButlerSnapshot> SnapshotAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var projects = await db.Projects.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var openIncidents = governance is null ? 0 : (await governance.GetAsync(tenantId, cancellationToken)).Incidents
            .Count(x => x.Status is "Open" or "Contained");
        return new ButlerSnapshot(
            projects.Count, employees.Count,
            await db.Approvals.CountAsync(x => x.TenantId == tenantId && x.State == ApprovalState.Pending, cancellationToken),
            await db.Candidates.CountAsync(x => x.TenantId == tenantId && x.Status != "Hired", cancellationToken),
            openIncidents,
            projects.Select(x => new ButlerProjectSnapshot(x.Name, x.Stage.ToString(), x.Health.ToString(), x.SpentMinor, x.BudgetMinor, x.Currency)).ToArray(),
            employees.GroupBy(x => x.State).ToDictionary(x => x.Key.ToString(), x => x.Count()));
    }

    private static string Classify(string question)
    {
        var lower = question.ToLowerInvariant();
        if (lower.Contains("budget") || lower.Contains("cost") || lower.Contains("费用") || lower.Contains("预算")) return "budget";
        if (lower.Contains("organization") || lower.Contains("employee") || lower.Contains("组织") || lower.Contains("员工")) return "organization";
        if (lower.Contains("project") || lower.Contains("项目")) return "projects";
        if (lower.Contains("approval") || lower.Contains("审批")) return "approvals";
        if (lower.Contains("hiring") || lower.Contains("candidate") || lower.Contains("招聘")) return "hiring";
        if (lower.Contains("security") || lower.Contains("incident") || lower.Contains("安全")) return "security";
        return "summary";
    }

    private static DateTimeOffset? NextRun(string frequency, DateTimeOffset now) => frequency switch
    {
        "Daily" => now.AddDays(1), "Weekly" => now.AddDays(7), _ => null
    };

    private sealed record ButlerSnapshot(int ProjectCount, int EmployeeCount, int PendingApprovals,
        int Candidates, int OpenIncidents, IReadOnlyList<ButlerProjectSnapshot> Projects,
        IReadOnlyDictionary<string, int> EmployeeStates);
    private sealed record ButlerProjectSnapshot(string Name, string Stage, string Health,
        long SpentMinor, long BudgetMinor, string Currency);
}
