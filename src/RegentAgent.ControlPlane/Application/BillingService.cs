using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Billing;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed record LocalPaymentResult(string PaymentRef, string Status);

public interface ISubscriptionPaymentAdapter
{
    string Provider { get; }
    Task<LocalPaymentResult> PayAsync(BillingInvoiceDto invoice, CancellationToken cancellationToken);
}

public sealed class LocalSubscriptionPaymentAdapter : ISubscriptionPaymentAdapter
{
    public string Provider => "local";
    public Task<LocalPaymentResult> PayAsync(BillingInvoiceDto invoice, CancellationToken cancellationToken) =>
        Task.FromResult(new LocalPaymentResult($"local-{invoice.Id:N}", "Paid"));
}

public sealed class BillingStateRecord
{
    public Guid TenantId { get; set; }
    public SubscriptionDto Subscription { get; set; } = null!;
    public List<BillingInvoiceDto> Invoices { get; set; } = [];
    public List<BillingLedgerEntry> Ledger { get; set; } = [];
}

public sealed class BillingService(RegentDbContext db, JsonDocumentStore<BillingStateRecord> store,
    AuditIntegrityService audit, ISubscriptionPaymentAdapter payments, TimeProvider clock) : ITenantQuotaGuard
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<BillingSummaryDto> GetSummaryAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var state = await LoadAsync(tenantId, cancellationToken);
        var plan = BillingPlans.Find(state.Subscription.PlanId);
        var employees = await db.Employees.CountAsync(x => x.TenantId == tenantId && x.State != RegentAgent.Contracts.Enums.EmployeeState.Terminated, cancellationToken);
        var projects = await db.Projects.CountAsync(x => x.TenantId == tenantId, cancellationToken);
        var storage = await db.Artifacts.Where(x => x.TenantId == tenantId).SumAsync(x => x.Length, cancellationToken);
        var quotas = new[]
        {
            new QuotaUsageDto("employees", employees, plan.MaxEmployees, employees > plan.MaxEmployees),
            new QuotaUsageDto("projects", projects, plan.MaxProjects, projects > plan.MaxProjects),
            new QuotaUsageDto("storage_bytes", storage, plan.MaxStorageBytes, storage > plan.MaxStorageBytes),
            new QuotaUsageDto("monthly_model_budget_minor", plan.MonthlyModelBudgetMinor, plan.MonthlyModelBudgetMinor, false)
        };
        return new BillingSummaryDto(state.Subscription, plan, quotas,
            state.Invoices.OrderByDescending(invoice => invoice.CreatedAt).ToArray(),
            state.Ledger.OrderBy(entry => entry.CreatedAt).ToArray());
    }

    public async Task<BillingInvoiceDto> CheckoutAsync(Guid userId, Guid tenantId, CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var plan = BillingPlans.Find(request.PlanId);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(tenantId, cancellationToken);
            var invoice = new BillingInvoiceDto(Guid.NewGuid(), tenantId, plan.Id, plan.MonthlyAmountMinor,
                plan.Currency, "Pending", payments.Provider, clock.GetUtcNow(), null, null);
            state.Invoices.Add(invoice);
            await store.PutAsync(tenantId.ToString(), state, cancellationToken);
            await audit.AppendAsync(tenantId, "billing.checkout_created", "Owner", new
            {
                actor_type = "Human", user_id = userId, invoice_id = invoice.Id, plan_id = plan.Id,
                amount_minor = invoice.AmountMinor, currency = invoice.Currency
            }, cancellationToken);
            return invoice;
        }
        finally { Gate.Release(); }
    }

    public async Task<BillingInvoiceDto> PayLocalAsync(Guid userId, Guid tenantId, Guid invoiceId,
        LocalPaymentRequest request, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        if (request.Confirmation != "PAY_LOCAL") throw new InvalidOperationException("Type PAY_LOCAL to confirm local payment.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(tenantId, cancellationToken);
            var invoice = state.Invoices.SingleOrDefault(item => item.Id == invoiceId)
                ?? throw new KeyNotFoundException("Invoice not found.");
            if (invoice.Status == "Paid") return invoice;
            if (invoice.Status != "Pending") throw new InvalidOperationException("Invoice is not payable.");
            var payment = await payments.PayAsync(invoice, cancellationToken);
            if (payment.Status != "Paid" || string.IsNullOrWhiteSpace(payment.PaymentRef))
                throw new InvalidOperationException("Payment adapter did not confirm payment.");
            invoice = invoice with { Status = "Paid", PaidAt = clock.GetUtcNow(), PaymentRef = payment.PaymentRef };
            state.Invoices[state.Invoices.FindIndex(item => item.Id == invoice.Id)] = invoice;
            var plan = BillingPlans.Find(invoice.PlanId);
            var now = clock.GetUtcNow();
            state.Subscription = new SubscriptionDto(tenantId, plan.Id, "Active", plan.Currency,
                new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1), state.Subscription.Version + 1);
            state.Ledger.Add(new BillingLedgerEntry(Guid.NewGuid(), tenantId, invoice.Id, "charge",
                invoice.AmountMinor, invoice.Currency, $"plan:{plan.Id}:v1", now));
            await store.PutAsync(tenantId.ToString(), state, cancellationToken);
            await audit.AppendAsync(tenantId, "billing.invoice_paid", "Owner", new
            {
                actor_type = "Human", user_id = userId, invoice_id = invoice.Id, plan_id = plan.Id,
                payment_provider = payments.Provider, amount_minor = invoice.AmountMinor, currency = invoice.Currency
            }, cancellationToken);
            return invoice;
        }
        finally { Gate.Release(); }
    }

    public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
        EnsureQuotaAsync(tenantId, resource, increment, cancellationToken);

    public async Task EnsureQuotaAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken)
    {
        if (increment < 0) throw new InvalidOperationException("Quota increment cannot be negative.");
        var state = await LoadAsync(tenantId, cancellationToken);
        var plan = BillingPlans.Find(state.Subscription.PlanId);
        var used = resource switch
        {
            "employees" => await db.Employees.CountAsync(x => x.TenantId == tenantId && x.State != RegentAgent.Contracts.Enums.EmployeeState.Terminated, cancellationToken),
            "projects" => await db.Projects.CountAsync(x => x.TenantId == tenantId, cancellationToken),
            "storage_bytes" => await db.Artifacts.Where(x => x.TenantId == tenantId).SumAsync(x => x.Length, cancellationToken),
            _ => throw new InvalidOperationException("Unknown quota resource.")
        };
        var limit = resource switch
        {
            "employees" => plan.MaxEmployees,
            "projects" => plan.MaxProjects,
            "storage_bytes" => plan.MaxStorageBytes,
            _ => 0
        };
        if (used + increment > limit)
            throw new InvalidOperationException($"Plan quota exceeded for {resource}: {used + increment}/{limit}.");
    }

    private async Task<BillingStateRecord> LoadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(tenantId.ToString(), cancellationToken);
        if (state is not null) return state;
        var now = clock.GetUtcNow();
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new BillingStateRecord
        {
            TenantId = tenantId,
            Subscription = new SubscriptionDto(tenantId, "starter", "Active", "CNY", start, start.AddMonths(1), 1)
        };
    }

    private async Task RequireOwnerAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = await db.Memberships.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.Role).SingleOrDefaultAsync(cancellationToken);
        if (role != "Owner") throw new UnauthorizedAccessException("Primary Owner permission required.");
    }
}
