namespace RegentAgent.Contracts.Operations;

public sealed class ObservabilityProfile
{
    public string Mode { get; set; } = "local";
    public string ServiceName { get; set; } = "regent-agent";
    public bool ElasticEnabled { get; set; }
    public int MetricIntervalSeconds { get; set; } = 15;
}

public sealed record ElasticsearchIndexDto(string Name, long? Documents, string? StoreSize);

public sealed record ElasticsearchStatusDto(
    bool Configured,
    bool Reachable,
    string? ClusterName,
    string? Status,
    string? Version,
    int? Nodes,
    int? ActivePrimaryShards,
    IReadOnlyList<ElasticsearchIndexDto> Indices,
    DateTimeOffset CheckedAt,
    string? Error = null);

public sealed class SloDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public decimal Target { get; set; }
    public int WindowDays { get; set; } = 30;
}

public sealed class TelemetrySample
{
    public string Metric { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public bool Success { get; set; } = true;
    public DateTimeOffset ObservedAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? TraceId { get; set; }
}

public sealed record SloEvaluationDto(string Name, decimal Actual, decimal Target, bool Passed, decimal ErrorBudgetRemaining,
    string Level = "L0", Guid? AlertId = null);

public sealed class AlertRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string SloName { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Level { get; set; } = "L1";
    public string Status { get; set; } = "Firing";
    public string Message { get; set; } = string.Empty;
    public string Runbook { get; set; } = "observability://runbook";
    public string? TraceId { get; set; }
    public string Owner { get; set; } = "SRE";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public sealed class AlertSilenceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Fingerprint { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record AlertAcknowledgementRequest(string Operator);
public sealed record AlertSilenceRequest(string Fingerprint, string Reason, string ApprovedBy, int Minutes);

public sealed class BrowserTelemetrySample
{
    public string Metric { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public bool Success { get; set; } = true;
    public string ServiceName { get; set; } = string.Empty;
    public string? TraceId { get; set; }
}

public sealed record BrowserTelemetryBatch(string ServiceName, IReadOnlyList<BrowserTelemetrySample> Samples);
