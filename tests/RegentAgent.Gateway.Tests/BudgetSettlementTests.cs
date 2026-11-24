using RegentAgent.Gateway.Metering;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class BudgetSettlementTests
{
    private static readonly PriceQuote Quote = new("openai", "priced-model", "price-1", "catalog-1",
        "CNY", 1_000m, 2_000m, 500m, DateTimeOffset.UnixEpoch, 100m);

    [Fact]
    public async Task Concurrent_reservations_cannot_exceed_the_hard_limit()
    {
        var root = NewRoot();
        try
        {
            var budget = new LocalBudgetService(1_000, "CNY", Path.Combine(root, "budget.json"));
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                budget.TryReserveAsync(Request("2026-11", 600), CancellationToken.None)));
            Assert.Single(results, item => item is not null);
            var snapshot = await budget.GetSnapshotAsync("tenant", "2026-11", CancellationToken.None);
            Assert.Equal(600, snapshot.ReservedMinor);
            Assert.Equal(400, snapshot.AvailableMinor);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Usage_is_idempotent_and_settlement_releases_the_reservation()
    {
        var root = NewRoot();
        try
        {
            var budget = new LocalBudgetService(1_000, "CNY", Path.Combine(root, "budget.json"));
            var reservation = (await budget.TryReserveAsync(Request("2026-11", 100), CancellationToken.None))!;
            var usage = new BudgetUsageRecord(reservation.Id, "usage-1", 40, DateTimeOffset.UtcNow, "provider-1", 1, true);
            await budget.RecordUsageAsync(usage, CancellationToken.None);
            await budget.RecordUsageAsync(usage, CancellationToken.None);
            var settled = await budget.CompleteAsync(reservation.Id, CancellationToken.None);
            Assert.Equal(40, settled.SettledMinor);
            Assert.Equal(0, settled.OverrunMinor);
            var snapshot = await budget.GetSnapshotAsync("tenant", "2026-11", CancellationToken.None);
            Assert.Equal(40, snapshot.CommittedMinor);
            Assert.Equal(0, snapshot.ReservedMinor);
            Assert.Single(await budget.ListLedgerAsync("tenant", "2026-11", null, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Late_usage_posts_to_the_current_period_but_belongs_to_the_occurred_period()
    {
        var root = NewRoot();
        try
        {
            var clock = new MutableClock(new DateTimeOffset(2026, 10, 31, 23, 0, 0, TimeSpan.Zero));
            var budget = new LocalBudgetService(10_000, "CNY", Path.Combine(root, "budget.json"), clock);
            var reservation = (await budget.TryReserveAsync(Request("2026-10", 100), CancellationToken.None))!;
            clock.UtcNow = new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero);
            await budget.RecordUsageAsync(new BudgetUsageRecord(reservation.Id, "late-usage", 25,
                new DateTimeOffset(2026, 10, 31, 23, 30, 0, TimeSpan.Zero), "provider", 1, true), CancellationToken.None);
            await budget.CompleteAsync(reservation.Id, CancellationToken.None);
            var entry = Assert.Single(await budget.ListLedgerAsync("tenant", null, null, CancellationToken.None));
            Assert.Equal("2026-10", entry.OccurredPeriod);
            Assert.Equal("2026-11", entry.PostedPeriod);
            Assert.Equal(25, (await budget.GetSnapshotAsync("tenant", "2026-10", CancellationToken.None)).CommittedMinor);
            Assert.Equal(0, (await budget.GetSnapshotAsync("tenant", "2026-11", CancellationToken.None)).CommittedMinor);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Expired_and_released_reservations_stop_consuming_available_budget()
    {
        var root = NewRoot();
        try
        {
            var clock = new MutableClock(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
            var budget = new LocalBudgetService(1_000, "CNY", Path.Combine(root, "budget.json"), clock);
            var expiring = (await budget.TryReserveAsync(Request("2026-11", 300, clock.UtcNow.AddMinutes(10)), CancellationToken.None))!;
            var released = (await budget.TryReserveAsync(Request("2026-11", 200, clock.UtcNow.AddMinutes(10)), CancellationToken.None))!;
            await budget.ReleaseAsync(released.Id, "Provider unavailable", CancellationToken.None);
            clock.UtcNow = clock.UtcNow.AddMinutes(11);
            Assert.Equal(1, await budget.ExpireAsync(CancellationToken.None));
            var snapshot = await budget.GetSnapshotAsync("tenant", "2026-11", CancellationToken.None);
            Assert.Equal(0, snapshot.ReservedMinor);
            Assert.Equal(1_000, snapshot.AvailableMinor);
            Assert.Equal("Expired", (await budget.CompleteAsync(expiring.Id, CancellationToken.None)).Status);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Reconciliation_appends_correction_without_mutating_original_entries()
    {
        var root = NewRoot();
        try
        {
            var budget = new LocalBudgetService(10_000, "CNY", Path.Combine(root, "budget.json"));
            var reservation = (await budget.TryReserveAsync(Request("2026-11", 500), CancellationToken.None))!;
            await budget.RecordUsageAsync(new BudgetUsageRecord(reservation.Id, "usage-1", 100,
                DateTimeOffset.UtcNow, "provider-1", 1, true), CancellationToken.None);
            await budget.CompleteAsync(reservation.Id, CancellationToken.None);
            var result = await budget.ReconcileAsync("tenant", new BudgetReconciliationRequest("2026-11", "openai",
                "CNY", 150, "invoice-1", "Provider billed cached tokens separately", 0, 0), CancellationToken.None);
            Assert.True(result.ThresholdExceeded);
            Assert.Equal(50, result.DifferenceMinor);
            Assert.NotNull(result.CorrectionEntryId);
            var ledger = await budget.ListLedgerAsync("tenant", "2026-11", null, CancellationToken.None);
            Assert.Equal(2, ledger.Count);
            Assert.Equal(100, ledger[0].AmountMinor);
            Assert.Equal(50, ledger[1].AmountMinor);
            Assert.Equal("correction", ledger[1].EntryType);
            var repeated = await budget.ReconcileAsync("tenant", new BudgetReconciliationRequest("2026-11", "openai",
                "CNY", 150, "invoice-1", "Replay", 0, 0), CancellationToken.None);
            Assert.Equal("Matched", repeated.Status);
            Assert.Null(repeated.CorrectionEntryId);
            Assert.Equal(2, (await budget.ListLedgerAsync("tenant", "2026-11", null, CancellationToken.None)).Count);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Price_correction_references_the_original_entry_and_unpriced_models_fail_closed()
    {
        var root = NewRoot();
        try
        {
            var budget = new LocalBudgetService(10_000, "CNY", Path.Combine(root, "budget.json"));
            var reservation = (await budget.TryReserveAsync(Request("2026-11", 500), CancellationToken.None))!;
            await budget.RecordUsageAsync(new BudgetUsageRecord(reservation.Id, "usage-1", 100,
                DateTimeOffset.UtcNow, "provider-1", 1, true), CancellationToken.None);
            var original = Assert.Single(await budget.ListLedgerAsync("tenant", null, null, CancellationToken.None));
            var correction = await budget.CorrectAsync("tenant", new BudgetCorrectionRequest(original.Id, -10, "CNY",
                "Corrected price snapshot"), CancellationToken.None);
            Assert.Equal(original.Id, correction.CorrectsEntryId);
            Assert.Equal("refund", correction.EntryType);
            Assert.Equal(100, original.AmountMinor);
            Assert.Throws<InvalidOperationException>(() => new ModelPriceBook([]).Quote("openai", "missing", DateTimeOffset.UtcNow));
        }
        finally { Delete(root); }
    }

    private static BudgetReservationRequest Request(string period, long amount, DateTimeOffset? expiresAt = null) => new("tenant", period, amount,
        "CNY", expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10), Guid.NewGuid().ToString(), "employee", "task", "openai", "priced-model", Quote);

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"budget-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class MutableClock(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
