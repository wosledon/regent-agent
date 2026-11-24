using System.Net.Http.Json;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Gateway.Metering;

public sealed record UsageProjectionRecord(
    ModelUsageProjection Projection,
    int Attempts,
    DateTimeOffset? LastAttemptAt,
    string? LastError,
    DateTimeOffset NextAttemptAt);

public interface IUsageProjector
{
    Task ProjectAsync(UsageRecord record, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

public sealed class HttpUsageProjector(
    JsonDocumentStore<UsageProjectionRecord> store,
    IHttpClientFactory clients,
    ILogger<HttpUsageProjector> logger) : IUsageProjector
{
    public async Task ProjectAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.UsageEventId) || record.ReservationId is null)
            throw new InvalidOperationException("Usage record is missing projection identity.");
        if (await store.GetAsync(record.UsageEventId, cancellationToken) is not null) return;
        var projection = new ModelUsageProjection(record.UsageEventId, record.TenantId, record.EmployeeId,
            record.TaskId, record.Provider, record.Model, record.InputTokens, record.OutputTokens,
            record.AmountMinor, record.MinorUnitFactor, record.Currency ?? string.Empty,
            record.PriceVersion ?? string.Empty, record.OccurredAt, record.RequestId, record.Status == "Succeeded");
        await store.PutAsync(record.UsageEventId, new UsageProjectionRecord(projection, 0, null, null,
            DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var pending = (await store.GetAllAsync(cancellationToken))
            .Where(item => item.NextAttemptAt <= DateTimeOffset.UtcNow).OrderBy(item => item.NextAttemptAt).ToArray();
        foreach (var item in pending)
        {
            try
            {
                var client = clients.CreateClient("controlplane-metering");
                using var response = await client.PostAsJsonAsync("/internal/metering/usage", item.Projection, cancellationToken);
                response.EnsureSuccessStatusCode();
                await store.DeleteAsync(item.Projection.UsageEventId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(item.Attempts, 8))));
                await store.PutAsync(item.Projection.UsageEventId, item with
                {
                    Attempts = item.Attempts + 1, LastAttemptAt = DateTimeOffset.UtcNow,
                    LastError = exception.GetType().Name, NextAttemptAt = DateTimeOffset.UtcNow.Add(delay)
                }, cancellationToken);
                logger.LogWarning("ControlPlane usage projection failed for {UsageEventId}; retry scheduled.",
                    item.Projection.UsageEventId);
            }
        }
    }
}

public sealed class UsageProjectionWorker(JsonDocumentStore<UsageProjectionRecord> store,
    IHttpClientFactory clients, ILoggerFactory loggerFactory)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var logger = loggerFactory.CreateLogger<UsageProjectionWorker>();
            try { await new HttpUsageProjector(store, clients, loggerFactory.CreateLogger<HttpUsageProjector>()).FlushAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Usage projection worker failed."); }
        }
    }
}
