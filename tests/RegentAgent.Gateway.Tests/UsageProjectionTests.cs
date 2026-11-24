using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using RegentAgent.Gateway.Metering;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class UsageProjectionTests
{
    [Fact]
    public async Task Projection_is_deduplicated_retried_and_removed_after_success()
    {
        var root = Path.Combine(Path.GetTempPath(), $"usage-projection-{Guid.NewGuid():N}");
        try
        {
            var attempts = 0;
            var handler = new FakeHttpMessageHandler((_, _) =>
            {
                attempts++;
                return Task.FromResult(attempts == 1
                    ? FakeHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.NoContent));
            });
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://controlplane.test") };
            var store = new JsonDocumentStore<UsageProjectionRecord>(Path.Combine(root, "projections.json"));
            var projector = new HttpUsageProjector(store, new FixedHttpClientFactory(http), NullLogger<HttpUsageProjector>.Instance);
            var record = new UsageRecord(DateTimeOffset.UtcNow, "tenant", "openai", "local-cheap", 3, 4, "provider-1")
            {
                ReservationId = Guid.NewGuid(), UsageEventId = "usage-1", RequestId = "request-1",
                AmountMinor = 0, MinorUnitFactor = 100m, Currency = "CNY", PriceVersion = "price-1"
            };

            await projector.ProjectAsync(record, CancellationToken.None);
            await projector.ProjectAsync(record, CancellationToken.None);
            await projector.FlushAsync(CancellationToken.None);
            var pending = Assert.Single(await store.GetAllAsync(CancellationToken.None));
            Assert.Equal(1, pending.Attempts);
            Assert.Equal("HttpRequestException", pending.LastError);
            Assert.Single(handler.Requests);
            await projector.ProjectAsync(record, CancellationToken.None);
            Assert.Equal(1, Assert.Single(await store.GetAllAsync(CancellationToken.None)).Attempts);

            pending = pending with { NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            await store.PutAsync("usage-1", pending, CancellationToken.None);
            await projector.FlushAsync(CancellationToken.None);

            Assert.Empty(await store.GetAllAsync(CancellationToken.None));
            Assert.Equal(2, handler.Requests.Count);
            Assert.Contains("usage-1", handler.Requests[1].Body);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
