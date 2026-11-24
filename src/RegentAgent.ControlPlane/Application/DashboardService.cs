using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Dashboard;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Operations;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class DashboardService(RegentDbContext db, ITenantContext tenant, TimeProvider clock,
    JsonDocumentStore<AlertRecord>? alertStore = null)
{
    public async Task<DashboardSummary> GetAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var now = clock.GetUtcNow();
        var company = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantId, cancellationToken);
        var projects = await db.Projects.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var approvals = (await db.Approvals.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.State == ApprovalState.Pending)
            .ToListAsync(cancellationToken)).OrderBy(x => x.ExpiresAt).ToList();
        var notifications = (await db.Notifications.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RecipientId == tenant.UserId.Value)
            .ToListAsync(cancellationToken)).OrderByDescending(x => x.CreatedAt).Take(10).ToList();
        var lastAudit = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Seq).FirstOrDefaultAsync(cancellationToken);

        var staleAfter = TimeSpan.FromMinutes(10);
        var projectFreshness = projects.Count == 0 ? Freshness.Unknown
            : projects.Max(x => FreshnessPolicy.Evaluate(x.UpdatedAt, now, staleAfter));
        var currencyGroups = projects.GroupBy(x => x.Currency).ToArray();
        var primaryCurrency = currencyGroups.Length == 1 ? currencyGroups[0].Key : null;
        var budget = primaryCurrency is null ? 0 : projects.Where(x => x.Currency == primaryCurrency).Sum(x => x.BudgetMinor);
        var spent = primaryCurrency is null ? 0 : projects.Where(x => x.Currency == primaryCurrency).Sum(x => x.SpentMinor);
        var activeEmployees = employees.Count(x => x.State is EmployeeState.Active or EmployeeState.Busy);
        var riskProjects = projects.Count(x => x.Health is ProjectHealth.AtRisk or ProjectHealth.Blocked or ProjectHealth.Failed);

        var cards = new[]
        {
            Mappings.ToCard("company", "公司状态", company.CompanyName, company.Status, Severity.Low,
                projectFreshness, projects.Count == 0 ? now : projects.Max(x => x.UpdatedAt), "/"),
            Mappings.ToCard("pending", "待我处理", approvals.Count == 0 ? "没有待审批事项" : "需要你的决策",
                approvals.Count.ToString(), approvals.Count == 0 ? Severity.Low : Severity.High,
                Freshness.Fresh, now, "/governance/approvals"),
            Mappings.ToCard("projects", "项目健康", $"{projects.Count} 个项目，{riskProjects} 个有风险",
                riskProjects.ToString(), riskProjects == 0 ? Severity.Low : Severity.High,
                projectFreshness, projects.Count == 0 ? now : projects.Max(x => x.UpdatedAt), "/projects"),
            Mappings.ToCard("budget", "预算成本",
                primaryCurrency is null ? "多币种，详见预算页" : $"已使用 {spent:N0} / {budget:N0} {primaryCurrency}",
                primaryCurrency is null ? null : $"{(budget == 0 ? 0 : spent * 100m / budget):0.0}%",
                primaryCurrency is not null && spent > budget * 8 / 10 ? Severity.High : Severity.Low,
                projectFreshness, projects.Count == 0 ? now : projects.Max(x => x.UpdatedAt), "/budget"),
            Mappings.ToCard("organization", "组织产能", $"{activeEmployees}/{employees.Count} 员工可用",
                employees.Count.ToString(), Severity.Low, Freshness.Fresh, now, "/organization"),
            Mappings.ToCard("risk", "风险审计", $"{notifications.Count(x => x.Level is "High" or "Critical")} 条高风险通知",
                notifications.Count(x => x.Level is "High" or "Critical").ToString(),
                notifications.Any(x => x.Level is "High" or "Critical") ? Severity.High : Severity.Low,
                Freshness.Fresh, now, "/governance/action-center"),
            Mappings.ToCard("decisions", "最近决策", lastAudit?.EventType ?? "暂无决策",
                lastAudit?.CreatedAt.ToString("u") ?? null, Severity.Low, Freshness.Fresh,
                lastAudit?.CreatedAt ?? now, "/journal")
        };

        var actions = approvals.Select(x => new ActionCenterItem(
            x.Id, "Approval", x.Title, x.Severity, x.Requester, x.CreatedAt, x.ExpiresAt,
            true, "Review evidence and decide", $"/governance/approvals/{x.Id}")).ToList();
        actions.AddRange(projects.Where(x => x.Health == ProjectHealth.Blocked).Select(x => new ActionCenterItem(
            x.Id, "Project", x.Name, Severity.High, x.Owner, x.UpdatedAt, null,
            false, "Resolve blocking dependency", $"/projects/{x.Id}")));
        if (alertStore is not null)
        {
            var alerts = (await alertStore.GetAllAsync(cancellationToken)).Where(alert => alert.Status != "Resolved"
                && (string.IsNullOrEmpty(alert.TenantId) || alert.TenantId == tenantId.ToString())).ToArray();
            actions.AddRange(alerts.Select(alert => new ActionCenterItem(alert.Id, "Observability",
                $"{alert.Level} {alert.SloName}", alert.Level == "L3" ? Severity.Critical
                    : alert.Level == "L2" ? Severity.High : Severity.Medium,
                alert.Owner, alert.CreatedAt, alert.UpdatedAt.Add(alert.Level == "L3" ? TimeSpan.FromMinutes(15)
                    : alert.Level == "L2" ? TimeSpan.FromHours(1) : TimeSpan.FromDays(1)),
                alert.Level == "L3", alert.Status == "Acknowledged" ? "Track recovery and record resolution"
                    : alert.Status == "Silenced" ? "Review approved silence and expiry" : "Acknowledge and follow the runbook",
                "/governance/action-center")));
        }

        return new DashboardSummary(company.CompanyName, company.Status, cards, actions, now);
    }
}
