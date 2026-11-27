using System.Text.Json.Serialization;
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
    string Href)
{
    public string? TitleKey { get; init; }
    public string? SummaryKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DashboardCardSummaryValues? SummaryValues { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DashboardMetricPresentation? MetricPresentation { get; init; }
}

public sealed record DashboardCardSummaryValues
{
    public int? Count { get; init; }
    public int? SecondaryCount { get; init; }
    public int? TotalCount { get; init; }
    public int? ActiveCount { get; init; }
    public long? SpentMinor { get; init; }
    public long? BudgetMinor { get; init; }
    public string? Currency { get; init; }
    public string? EventType { get; init; }
}

public sealed record DashboardMetricPresentation
{
    public required string Kind { get; init; }
    public decimal? Value { get; init; }
    public string? Currency { get; init; }
    public DateTimeOffset? Timestamp { get; init; }
    public string? Text { get; init; }
}

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
    string Href,
    string? NextActionKey = null);
