namespace RegentAgent.Contracts.Billing;

public sealed record PlanDefinition(
    string Id,
    string Name,
    long MonthlyAmountMinor,
    string Currency,
    int MaxEmployees,
    int MaxProjects,
    long MaxStorageBytes,
    long MonthlyModelBudgetMinor);

public sealed record SubscriptionDto(
    Guid TenantId,
    string PlanId,
    string Status,
    string Currency,
    DateTimeOffset CurrentPeriodStart,
    DateTimeOffset CurrentPeriodEnd,
    int Version);

public sealed record QuotaUsageDto(string Resource, long Used, long Limit, bool Exceeded);

public sealed record BillingInvoiceDto(
    Guid Id,
    Guid TenantId,
    string PlanId,
    long AmountMinor,
    string Currency,
    string Status,
    string PaymentProvider,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? PaymentRef);

public sealed record BillingLedgerEntry(
    Guid Id,
    Guid TenantId,
    Guid InvoiceId,
    string EntryType,
    long AmountMinor,
    string Currency,
    string PriceVersion,
    DateTimeOffset CreatedAt);

public sealed record BillingSummaryDto(
    SubscriptionDto Subscription,
    PlanDefinition Plan,
    IReadOnlyList<QuotaUsageDto> Quotas,
    IReadOnlyList<BillingInvoiceDto> Invoices,
    IReadOnlyList<BillingLedgerEntry> Ledger);

public sealed record CheckoutRequest(string PlanId);
public sealed record LocalPaymentRequest(string Confirmation);

public interface ITenantQuotaGuard
{
    Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken);
}

public static class BillingPlans
{
    public static readonly PlanDefinition Starter = new("starter", "Starter", 0, "CNY", 5, 3, 1_073_741_824, 800_000);
    public static readonly PlanDefinition Pro = new("pro", "Pro", 29_900, "CNY", 20, 20, 10_737_418_240, 8_000_000);
    public static readonly PlanDefinition Enterprise = new("enterprise", "Enterprise", 99_900, "CNY", 100, 100, 107_374_182_400, 80_000_000);
    public static IReadOnlyList<PlanDefinition> All { get; } = [Starter, Pro, Enterprise];
    public static PlanDefinition Find(string id) => All.SingleOrDefault(plan => plan.Id == id)
        ?? throw new InvalidOperationException("Unknown subscription plan.");
}
