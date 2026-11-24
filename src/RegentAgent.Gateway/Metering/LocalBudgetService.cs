using System.Text.Json;

namespace RegentAgent.Gateway.Metering;

public sealed class LocalBudgetService(long monthlyLimit, string currency, string path, TimeProvider? clock = null)
{
    private readonly long monthlyLimit = monthlyLimit;
    private readonly string currency = currency;
    private readonly string path = path;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task<BudgetReservationRecord?> TryReserveAsync(BudgetReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AmountMinor < 0) throw new InvalidOperationException("Reservation amount cannot be negative.");
        if (!request.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Budget currency does not match the tenant budget currency.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var now = clock.GetUtcNow();
            ExpireReservations(state, now);
            var committed = state.Ledger.Where(entry => entry.TenantId == request.TenantId
                && entry.OccurredPeriod == request.Period && entry.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase))
                .Sum(entry => entry.AmountMinor);
            var reserved = state.Reservations.Where(reservation => reservation.TenantId == request.TenantId
                && reservation.Period == request.Period && reservation.Status == "Reserved").Sum(reservation => reservation.AmountMinor);
            if (committed + reserved + request.AmountMinor > monthlyLimit) return null;
            var reservation = new BudgetReservationRecord
            {
                TenantId = request.TenantId, Period = request.Period, AmountMinor = request.AmountMinor,
                Currency = request.Currency, CreatedAt = now, ExpiresAt = request.ExpiresAt,
                RequestId = request.RequestId, EmployeeId = request.EmployeeId, TaskId = request.TaskId,
                Provider = request.Provider, Model = request.Model, Quote = request.Quote
            };
            state.Reservations.Add(reservation);
            await SaveAsync(state, cancellationToken);
            return reservation;
        }
        finally { gate.Release(); }
    }

    public async Task RecordUsageAsync(BudgetUsageRecord usage, CancellationToken cancellationToken)
    {
        if (usage.AmountMinor < 0) throw new InvalidOperationException("Usage amount cannot be negative.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            if (state.Ledger.Any(entry => entry.UsageEventId == usage.UsageEventId)) return;
            var reservation = state.Reservations.SingleOrDefault(item => item.Id == usage.ReservationId)
                ?? throw new KeyNotFoundException("Budget reservation not found.");
            var quote = reservation.Quote ?? throw new InvalidOperationException("Reservation has no price snapshot.");
            var entry = new BudgetLedgerEntry(Guid.NewGuid(), reservation.TenantId, reservation.Id, usage.UsageEventId,
                null, ModelPricingService.Period(usage.OccurredAt), ModelPricingService.Period(clock.GetUtcNow()),
                "charge", usage.AmountMinor, quote.Currency, quote.PriceVersion, reservation.EmployeeId,
                reservation.TaskId, reservation.Provider, reservation.Model,
                usage.Succeeded ? "Provider usage" : usage.ErrorCode ?? "Provider attempt failed", clock.GetUtcNow());
            state.Ledger.Add(entry);
            reservation.SettledAmountMinor += usage.AmountMinor;
            if (reservation.SettledAmountMinor > reservation.AmountMinor
                && !state.Events.Any(item => item.TenantId == reservation.TenantId && item.Period == reservation.Period
                    && item.Type == "budget_overrun" && item.Message.Contains(reservation.Id.ToString(), StringComparison.Ordinal)))
                state.Events.Add(new BudgetEventRecord(Guid.NewGuid(), reservation.TenantId, reservation.Period,
                    "budget_overrun", $"Reservation {reservation.Id} exceeded its reserved amount.", clock.GetUtcNow()));
            await SaveAsync(state, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<BudgetSettlementResult> CompleteAsync(Guid reservationId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var reservation = state.Reservations.SingleOrDefault(item => item.Id == reservationId)
                ?? throw new KeyNotFoundException("Budget reservation not found.");
            if (reservation.Status == "Reserved" || (reservation.Status == "Expired" && reservation.SettledAmountMinor != 0))
                reservation.Status = "Settled";
            reservation.CompletedAt ??= clock.GetUtcNow();
            await SaveAsync(state, cancellationToken);
            return Result(reservation);
        }
        finally { gate.Release(); }
    }

    public async Task ReleaseAsync(Guid reservationId, string reason, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var reservation = state.Reservations.SingleOrDefault(item => item.Id == reservationId)
                ?? throw new KeyNotFoundException("Budget reservation not found.");
            if (reservation.Status is "Reserved" or "Expired")
            {
                reservation.Status = "Released";
                reservation.CompletedAt = clock.GetUtcNow();
                state.Events.Add(new BudgetEventRecord(Guid.NewGuid(), reservation.TenantId, reservation.Period,
                    "budget_released", $"{reason}: {reservation.Id}", clock.GetUtcNow()));
            }
            await SaveAsync(state, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var count = ExpireReservations(state, clock.GetUtcNow());
            if (count > 0) await SaveAsync(state, cancellationToken);
            return count;
        }
        finally { gate.Release(); }
    }

    public async Task<BudgetSnapshot> GetSnapshotAsync(string tenantId, string? period,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var now = clock.GetUtcNow();
            ExpireReservations(state, now);
            period ??= ModelPricingService.Period(now);
            var committed = state.Ledger.Where(entry => entry.TenantId == tenantId && entry.OccurredPeriod == period
                && entry.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.AmountMinor);
            var active = state.Reservations.Where(item => item.TenantId == tenantId && item.Period == period
                && item.Status == "Reserved").ToArray();
            var reserved = active.Sum(item => item.AmountMinor);
            return new BudgetSnapshot(tenantId, period, currency, monthlyLimit, committed, reserved,
                Math.Max(0, monthlyLimit - committed - reserved), active.Length, now);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<BudgetLedgerEntry>> ListLedgerAsync(string tenantId, string? period,
        string? provider, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            return state.Ledger.Where(entry => entry.TenantId == tenantId
                && (period is null || entry.OccurredPeriod == period)
                && (provider is null || entry.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(entry => entry.CreatedAt).ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<BudgetReconciliationResult> ReconcileAsync(string tenantId,
        BudgetReconciliationRequest request, CancellationToken cancellationToken)
    {
        if (request.ProviderAmountMinor < 0 || request.ThresholdBasisPoints is < 0 or > 10_000
            || request.ThresholdAbsoluteMinor < 0 || string.IsNullOrWhiteSpace(request.InvoiceRef)
            || string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Reconciliation values are invalid.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var now = clock.GetUtcNow();
            var local = state.Ledger.Where(entry => entry.TenantId == tenantId && entry.OccurredPeriod == request.Period
                && entry.Provider.Equals(request.Provider, StringComparison.OrdinalIgnoreCase)
                && entry.Currency.Equals(request.Currency, StringComparison.OrdinalIgnoreCase)).Sum(entry => entry.AmountMinor);
            var difference = request.ProviderAmountMinor - local;
            var threshold = Math.Max(request.ThresholdAbsoluteMinor,
                (long)decimal.Ceiling(Math.Abs(local) * request.ThresholdBasisPoints / 10_000m));
            var exceeded = difference != 0 && Math.Abs(difference) > threshold;
            Guid? correctionId = null;
            if (difference != 0)
            {
                var correction = new BudgetLedgerEntry(Guid.NewGuid(), tenantId, null, null, null, request.Period,
                    ModelPricingService.Period(now), difference < 0 ? "refund" : "correction", difference,
                    request.Currency, "provider-invoice", null, null, request.Provider, string.Empty,
                    $"{request.InvoiceRef}: {request.Reason}", now);
                state.Ledger.Add(correction);
                correctionId = correction.Id;
            }
            var record = new ReconciliationCaseRecord(Guid.NewGuid(), tenantId, request.Period, request.Provider,
                request.Currency, local, request.ProviderAmountMinor, difference, exceeded,
                difference == 0 ? "Matched" : "CorrectionPosted", request.InvoiceRef, correctionId, now);
            state.Reconciliations.Add(record);
            if (exceeded)
                state.Events.Add(new BudgetEventRecord(Guid.NewGuid(), tenantId, request.Period,
                    "provider_reconciliation_variance", $"{request.Provider} invoice {request.InvoiceRef} differs by {difference} {request.Currency}.", now));
            await SaveAsync(state, cancellationToken);
            return new BudgetReconciliationResult(record.Id, local, request.ProviderAmountMinor, difference,
                exceeded, record.Status, correctionId);
        }
        finally { gate.Release(); }
    }

    public async Task<BudgetLedgerEntry> CorrectAsync(string tenantId, BudgetCorrectionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AmountDeltaMinor == 0 || string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Correction amount and reason are required.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            var original = state.Ledger.SingleOrDefault(entry => entry.Id == request.CorrectsEntryId && entry.TenantId == tenantId)
                ?? throw new KeyNotFoundException("Ledger entry not found.");
            if (!original.Currency.Equals(request.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Correction currency does not match the original entry.");
            var correction = new BudgetLedgerEntry(Guid.NewGuid(), tenantId, original.ReservationId, null,
                original.Id, original.OccurredPeriod, ModelPricingService.Period(clock.GetUtcNow()),
                request.AmountDeltaMinor < 0 ? "refund" : "correction", request.AmountDeltaMinor,
                request.Currency, original.PriceVersion, original.EmployeeId, original.TaskId,
                original.Provider, original.Model, request.Reason, clock.GetUtcNow());
            state.Ledger.Add(correction);
            await SaveAsync(state, cancellationToken);
            return correction;
        }
        finally { gate.Release(); }
    }

    private static int ExpireReservations(BudgetStateRecord state, DateTimeOffset now)
    {
        var expired = state.Reservations.Where(item => item.Status == "Reserved" && item.ExpiresAt <= now).ToArray();
        foreach (var reservation in expired) reservation.Status = "Expired";
        return expired.Length;
    }

    private static BudgetSettlementResult Result(BudgetReservationRecord reservation) => new(
        reservation.Id, reservation.Status, reservation.AmountMinor, reservation.SettledAmountMinor,
        Math.Max(0, reservation.SettledAmountMinor - reservation.AmountMinor));

    private async Task<BudgetStateRecord> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new BudgetStateRecord();
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BudgetStateRecord>(stream, JsonOptions, cancellationToken)
            ?? new BudgetStateRecord();
    }

    private async Task SaveAsync(BudgetStateRecord state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(temp, path, true);
    }
}
