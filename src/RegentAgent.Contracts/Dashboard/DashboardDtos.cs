using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Dashboard;

public sealed record DashboardCard(
    string Id,
    string Title,
    string Summary,
    string? Metric,
    Severity Severity,
    Freshness Freshness,
    DateTimeOffset DataAt,
    string Href);

public sealed record DashboardSummary(
    string CompanyName,
    string CompanyStatus,
    IReadOnlyList<DashboardCard> Cards,
    IReadOnlyList<ActionCenterItem> Actions,
    DateTimeOffset GeneratedAt);

public sealed record ActionCenterItem(
    Guid Id,
    string SourceType,
    string Title,
    Severity Severity,
    string Owner,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SlaDueAt,
    bool RequiresBossDecision,
    string NextAction,
    string Href);
