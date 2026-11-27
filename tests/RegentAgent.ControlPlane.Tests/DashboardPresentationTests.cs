using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Dashboard;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Operations;
using RegentAgent.Contracts.Primitives;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class DashboardPresentationTests
{
    [Fact]
    public async Task Cards_emit_stable_keys_and_structured_values_without_replacing_legacy_copy()
    {
        await using var db = CreateDb();
        await DbInitializer.InitializeAsync(db, CancellationToken.None);

        var summary = await new DashboardService(db, new TestTenantContext(), new FixedTimeProvider()).GetAsync(CancellationToken.None);

        Assert.Equal(7, summary.Cards.Count);
        Assert.Equal(new[]
        {
            "dashboardPage.card.company.title", "dashboardPage.card.pending.title", "dashboardPage.card.projects.title",
            "dashboardPage.card.budget.title", "dashboardPage.card.organization.title", "dashboardPage.card.risk.title",
            "dashboardPage.card.decisions.title"
        }, summary.Cards.Select(card => card.TitleKey));
        Assert.All(summary.Cards, card =>
        {
            Assert.False(string.IsNullOrWhiteSpace(card.Title));
            Assert.False(string.IsNullOrWhiteSpace(card.Summary));
            Assert.StartsWith("/", card.Href);
        });

        var company = summary.Cards.Single(card => card.Id == "company");
        Assert.Equal(summary.CompanyName, company.Summary);
        Assert.Equal(summary.CompanyStatus, company.Metric);
        Assert.Equal("text", company.MetricPresentation?.Kind);
        Assert.Equal(summary.CompanyStatus, company.MetricPresentation?.Text);

        var pending = summary.Cards.Single(card => card.Id == "pending");
        Assert.Equal("dashboardPage.card.pending.summary", pending.SummaryKey);
        Assert.Equal(2, pending.SummaryValues?.Count);
        Assert.Equal("count", pending.MetricPresentation?.Kind);
        Assert.Equal(2m, pending.MetricPresentation?.Value);

        var projects = summary.Cards.Single(card => card.Id == "projects");
        Assert.Equal(3, projects.SummaryValues?.Count);
        Assert.Equal(2, projects.SummaryValues?.SecondaryCount);
        Assert.Equal(2m, projects.MetricPresentation?.Value);

        var budget = summary.Cards.Single(card => card.Id == "budget");
        Assert.Equal("dashboardPage.card.budget.summary", budget.SummaryKey);
        Assert.Equal(834_000L, budget.SummaryValues?.SpentMinor);
        Assert.Equal(1_650_000L, budget.SummaryValues?.BudgetMinor);
        Assert.Equal("CNY", budget.SummaryValues?.Currency);
        Assert.Equal("percent", budget.MetricPresentation?.Kind);
        Assert.InRange(budget.MetricPresentation!.Value!.Value, 0.5054m, 0.5055m);
        Assert.Contains("CNY", budget.Summary);

        var organization = summary.Cards.Single(card => card.Id == "organization");
        Assert.Equal(3, organization.SummaryValues?.ActiveCount);
        Assert.Equal(4, organization.SummaryValues?.TotalCount);
        Assert.Equal(4m, organization.MetricPresentation?.Value);

        var risk = summary.Cards.Single(card => card.Id == "risk");
        Assert.Equal(1, risk.SummaryValues?.Count);
        Assert.Equal(1m, risk.MetricPresentation?.Value);

        var decision = summary.Cards.Single(card => card.Id == "decisions");
        var lastAudit = await db.AuditRecords.OrderByDescending(item => item.Seq).SingleAsync();
        Assert.Equal("dashboardPage.card.decisions.summary", decision.SummaryKey);
        Assert.Equal(lastAudit.EventType, decision.SummaryValues?.EventType);
        Assert.Equal(lastAudit.EventType, decision.Summary);
        Assert.Equal("timestamp", decision.MetricPresentation?.Kind);
        Assert.Equal(lastAudit.CreatedAt, decision.MetricPresentation?.Timestamp);
    }

    [Fact]
    public async Task Approval_and_blocked_project_actions_keep_domain_data_and_add_next_action_keys()
    {
        await using var db = CreateDb();
        await DbInitializer.InitializeAsync(db, CancellationToken.None);

        var summary = await new DashboardService(db, new TestTenantContext(), new FixedTimeProvider()).GetAsync(CancellationToken.None);

        Assert.Equal(2, summary.Actions.Count(action => action.SourceType == "Approval"));
        Assert.All(summary.Actions.Where(action => action.SourceType == "Approval"), action =>
        {
            Assert.Equal("dashboardPage.action.approval.review", action.NextActionKey);
            Assert.Equal("Review evidence and decide", action.NextAction);
            Assert.NotEqual(Guid.Empty, action.Id);
            Assert.NotEmpty(action.Title);
        });
        var blocked = Assert.Single(summary.Actions, action => action.SourceType == "Project");
        Assert.Equal("Memory Poisoning Defense", blocked.Title);
        Assert.Equal("dashboardPage.action.project.resolveBlockingDependency", blocked.NextActionKey);
        Assert.Equal("Resolve blocking dependency", blocked.NextAction);
    }

    [Fact]
    public async Task Observability_actions_cover_each_status_and_exclude_other_tenants_and_resolved_alerts()
    {
        await using var db = CreateDb();
        await DbInitializer.InitializeAsync(db, CancellationToken.None);
        var tenantId = new TestTenantContext().TenantId.Value;
        var now = new FixedTimeProvider().GetUtcNow();
        var path = Path.Combine(Path.GetTempPath(), $"dashboard-alerts-{Guid.NewGuid():N}.json");
        var store = new JsonDocumentStore<AlertRecord>(path);
        var firing = new AlertRecord { TenantId = tenantId.ToString(), SloName = "API latency", Level = "L1", Status = "Firing", CreatedAt = now, UpdatedAt = now };
        var acknowledged = new AlertRecord { TenantId = tenantId.ToString(), SloName = "Error rate", Level = "L2", Status = "Acknowledged", CreatedAt = now, UpdatedAt = now };
        var silenced = new AlertRecord { TenantId = tenantId.ToString(), SloName = "Saturation", Level = "L3", Status = "Silenced", CreatedAt = now, UpdatedAt = now };
        var resolved = new AlertRecord { TenantId = tenantId.ToString(), SloName = "Resolved", Status = "Resolved", CreatedAt = now, UpdatedAt = now };
        var otherTenant = new AlertRecord { TenantId = Guid.NewGuid().ToString(), SloName = "Other tenant", Status = "Firing", CreatedAt = now, UpdatedAt = now };
        try
        {
            foreach (var alert in new[] { firing, acknowledged, silenced, resolved, otherTenant })
                await store.PutAsync(alert.Id.ToString(), alert, CancellationToken.None);

            var summary = await new DashboardService(db, new TestTenantContext(), new FixedTimeProvider(), store)
                .GetAsync(CancellationToken.None);

            Assert.Equal("dashboardPage.action.observability.acknowledgeRunbook", summary.Actions.Single(action => action.Id == firing.Id).NextActionKey);
            var acknowledgedAction = summary.Actions.Single(action => action.Id == acknowledged.Id);
            Assert.Equal("dashboardPage.action.observability.trackRecovery", acknowledgedAction.NextActionKey);
            Assert.Equal("Track recovery and record resolution", acknowledgedAction.NextAction);
            var silencedAction = summary.Actions.Single(action => action.Id == silenced.Id);
            Assert.Equal("dashboardPage.action.observability.reviewSilence", silencedAction.NextActionKey);
            Assert.Equal("Review approved silence and expiry", silencedAction.NextAction);
            Assert.DoesNotContain(summary.Actions, action => action.Id == resolved.Id || action.Id == otherTenant.Id);
            Assert.All(summary.Actions, action => Assert.False(string.IsNullOrWhiteSpace(action.NextActionKey)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Empty_card_branches_keep_legacy_text_and_omit_unavailable_structured_values()
    {
        await using var db = CreateDb();
        await DbInitializer.InitializeAsync(db, CancellationToken.None);
        var tenantId = new TestTenantContext().TenantId.Value;
        db.Projects.RemoveRange(db.Projects.Where(item => item.TenantId == tenantId));
        db.Approvals.RemoveRange(db.Approvals.Where(item => item.TenantId == tenantId));
        db.Notifications.RemoveRange(db.Notifications.Where(item => item.TenantId == tenantId));
        db.AuditRecords.RemoveRange(db.AuditRecords.Where(item => item.TenantId == tenantId));
        await db.SaveChangesAsync();

        var summary = await new DashboardService(db, new TestTenantContext(), new FixedTimeProvider()).GetAsync(CancellationToken.None);

        var pending = summary.Cards.Single(card => card.Id == "pending");
        Assert.Equal("没有待审批事项", pending.Summary);
        Assert.Equal("dashboardPage.card.pending.empty", pending.SummaryKey);
        Assert.Null(pending.SummaryValues);
        Assert.Equal(0m, pending.MetricPresentation?.Value);

        var budget = summary.Cards.Single(card => card.Id == "budget");
        Assert.Equal("多币种，详见预算页", budget.Summary);
        Assert.Equal("dashboardPage.card.budget.multiCurrency", budget.SummaryKey);
        Assert.Null(budget.SummaryValues);
        Assert.Null(budget.MetricPresentation);

        var decision = summary.Cards.Single(card => card.Id == "decisions");
        Assert.Equal("暂无决策", decision.Summary);
        Assert.Equal("dashboardPage.card.decisions.empty", decision.SummaryKey);
        Assert.Null(decision.SummaryValues);
        Assert.Null(decision.MetricPresentation);
    }

    [Fact]
    public void Presentation_fields_are_optional_and_round_trip_when_present()
    {
        var legacy = new DashboardCard("legacy", "Legacy title", "Legacy summary", "Legacy metric",
            Severity.Low, Freshness.Unknown, DateTimeOffset.UtcNow, "/legacy");
        var legacyAction = new ActionCenterItem(Guid.NewGuid(), "Approval", "Domain title", Severity.Low,
            "Owner", DateTimeOffset.UtcNow, null, false, "Legacy action", "/legacy");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var legacyDocument = JsonDocument.Parse(JsonSerializer.Serialize(legacy, options));
        var legacyRoot = legacyDocument.RootElement;
        Assert.Equal(JsonValueKind.Null, legacyRoot.GetProperty("titleKey").ValueKind);
        Assert.Equal(JsonValueKind.Null, legacyRoot.GetProperty("summaryKey").ValueKind);
        Assert.False(legacyRoot.TryGetProperty("summaryValues", out _));
        Assert.False(legacyRoot.TryGetProperty("metricPresentation", out _));
        Assert.Equal("Legacy title", legacyRoot.GetProperty("title").GetString());
        Assert.Equal("Legacy summary", legacyRoot.GetProperty("summary").GetString());
        Assert.Equal("Legacy metric", legacyRoot.GetProperty("metric").GetString());

        using var legacyActionDocument = JsonDocument.Parse(JsonSerializer.Serialize(legacyAction, options));
        Assert.Equal(JsonValueKind.Null, legacyActionDocument.RootElement.GetProperty("nextActionKey").ValueKind);
        Assert.Equal("Legacy action", legacyActionDocument.RootElement.GetProperty("nextAction").GetString());

        var structured = legacy with
        {
            TitleKey = "dashboardPage.card.legacy.title",
            SummaryKey = "dashboardPage.card.legacy.summary",
            SummaryValues = new DashboardCardSummaryValues { Count = 1 },
            MetricPresentation = new DashboardMetricPresentation { Kind = "count", Value = 1 }
        };
        var roundTrip = JsonSerializer.Deserialize<DashboardCard>(JsonSerializer.Serialize(structured, options), options);
        Assert.NotNull(roundTrip);
        Assert.Equal(structured.TitleKey, roundTrip.TitleKey);
        Assert.Equal(1, roundTrip.SummaryValues?.Count);
        Assert.Equal(1m, roundTrip.MetricPresentation?.Value);
        Assert.Equal("Legacy title", roundTrip.Title);
        Assert.Equal("Legacy summary", roundTrip.Summary);
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestTenantContext : ITenantContext
    {
        public TenantId TenantId { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        public UserId UserId { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        public string DisplayName => "Demo Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
    }
}
