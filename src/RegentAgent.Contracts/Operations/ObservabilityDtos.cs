namespace RegentAgent.Contracts.Operations;

public sealed class ObservabilityProfile
{
    public string Mode { get; set; } = "local";
    public string ServiceName { get; set; } = "regent-agent";
    public bool ElasticEnabled { get; set; }
    public int MetricIntervalSeconds { get; set; } = 15;
}

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
}

public sealed record SloEvaluationDto(string Name, decimal Actual, decimal Target, bool Passed, decimal ErrorBudgetRemaining);
