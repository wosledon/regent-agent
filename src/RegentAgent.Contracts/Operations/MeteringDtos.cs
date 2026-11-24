namespace RegentAgent.Contracts.Operations;

public sealed record ModelUsageProjection(
    string UsageEventId,
    string TenantId,
    string? EmployeeId,
    string? TaskId,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    long AmountMinor,
    decimal MinorUnitFactor,
    string Currency,
    string PriceVersion,
    DateTimeOffset OccurredAt,
    string? RequestId,
    bool Succeeded);
