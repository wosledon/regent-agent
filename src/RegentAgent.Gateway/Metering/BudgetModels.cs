namespace RegentAgent.Gateway.Metering;

public sealed record ModelPriceSnapshot(
    string Provider,
    string Model,
    string PriceVersion,
    string CatalogSnapshotId,
    string Currency,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    decimal? CachedInputPricePerMillion,
    DateTimeOffset EffectiveAt);

public sealed record PriceQuote(
    string Provider,
    string Model,
    string PriceVersion,
    string CatalogSnapshotId,
    string Currency,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    decimal CachedInputPricePerMillion,
    DateTimeOffset EffectiveAt,
    decimal MinorUnitFactor);

public sealed record BudgetReservationRequest(
    string TenantId,
    string Period,
    long AmountMinor,
    string Currency,
    DateTimeOffset ExpiresAt,
    string RequestId,
    string? EmployeeId,
    string? TaskId,
    string Provider,
    string Model,
    PriceQuote Quote);

public sealed class BudgetReservationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TenantId { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public long AmountMinor { get; set; }
    public long SettledAmountMinor { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = "Reserved";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public string? TaskId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public PriceQuote? Quote { get; set; }
}

public sealed record BudgetLedgerEntry(
    Guid Id,
    string TenantId,
    Guid? ReservationId,
    string? UsageEventId,
    Guid? CorrectsEntryId,
    string OccurredPeriod,
    string PostedPeriod,
    string EntryType,
    long AmountMinor,
    string Currency,
    string PriceVersion,
    string? EmployeeId,
    string? TaskId,
    string Provider,
    string Model,
    string Reason,
    DateTimeOffset CreatedAt);

public sealed record BudgetEventRecord(
    Guid Id,
    string TenantId,
    string Period,
    string Type,
    string Message,
    DateTimeOffset CreatedAt);

public sealed record ReconciliationCaseRecord(
    Guid Id,
    string TenantId,
    string Period,
    string Provider,
    string Currency,
    long LocalAmountMinor,
    long ProviderAmountMinor,
    long DifferenceMinor,
    bool ThresholdExceeded,
    string Status,
    string InvoiceRef,
    Guid? CorrectionEntryId,
    DateTimeOffset CreatedAt);

public sealed class BudgetStateRecord
{
    public List<BudgetReservationRecord> Reservations { get; set; } = [];
    public List<BudgetLedgerEntry> Ledger { get; set; } = [];
    public List<BudgetEventRecord> Events { get; set; } = [];
    public List<ReconciliationCaseRecord> Reconciliations { get; set; } = [];
}

public sealed record BudgetSnapshot(
    string TenantId,
    string Period,
    string Currency,
    long MonthlyLimitMinor,
    long CommittedMinor,
    long ReservedMinor,
    long AvailableMinor,
    int ActiveReservations,
    DateTimeOffset EvaluatedAt);

public sealed record BudgetUsageRecord(
    Guid ReservationId,
    string UsageEventId,
    long AmountMinor,
    DateTimeOffset OccurredAt,
    string? ProviderRequestId,
    int Attempt,
    bool Succeeded,
    string? ErrorCode = null);

public sealed record BudgetSettlementResult(Guid ReservationId, string Status, long ReservedMinor, long SettledMinor, long OverrunMinor);

public sealed record BudgetReconciliationRequest(
    string Period,
    string Provider,
    string Currency,
    long ProviderAmountMinor,
    string InvoiceRef,
    string Reason,
    int ThresholdBasisPoints = 100,
    long ThresholdAbsoluteMinor = 100);

public sealed record BudgetReconciliationResult(
    Guid CaseId,
    long LocalAmountMinor,
    long ProviderAmountMinor,
    long DifferenceMinor,
    bool ThresholdExceeded,
    string Status,
    Guid? CorrectionEntryId);

public sealed record BudgetCorrectionRequest(
    Guid CorrectsEntryId,
    long AmountDeltaMinor,
    string Currency,
    string Reason);
