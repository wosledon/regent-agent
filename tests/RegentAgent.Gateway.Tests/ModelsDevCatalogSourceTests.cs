using System.Net;
using RegentAgent.Providers;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class ModelsDevCatalogSourceTests
{
    [Fact]
    public async Task Models_dev_snapshot_is_checksummed_and_filters_supported_providers()
    {
        const string catalog = """
        {
          "openai": { "models": { "gpt-test": { "id": "gpt-test", "version": "1", "cost": { "input": 0.5, "output": 1.5 } } } },
          "anthropic": { "models": { "claude-test": { "id": "claude-test", "last_updated": "2026-11", "cost": { "input": 1 } } } },
          "google": { "models": { "gemini-test": { "id": "gemini-test", "cost": { "input": 1, "output": 2 } } } }
        }
        """;
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json(catalog)));
        using var http = new HttpClient(handler);
        var source = new ModelsDevCatalogSource(http, new ModelsDevCatalogOptions
        {
            Url = "http://localhost/api.json"
        });

        var snapshot = await source.FetchAsync(new DateOnly(2026, 11, 24), CancellationToken.None);

        Assert.Equal(2, snapshot.Entries.Count);
        Assert.Contains(snapshot.Entries, entry => entry.Provider == "OpenAI" && entry.PriceState == "Priced"
            && entry.InputPricePerMillion == 0.5m && entry.OutputPricePerMillion == 1.5m);
        Assert.Contains(snapshot.Entries, entry => entry.Provider == "Anthropic" && entry.PriceState == "Unpriced");
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Provider == "Google");
        Assert.Equal(catalog, snapshot.RawJson);
        Assert.Equal(64, snapshot.Checksum.Length);
        Assert.Equal(catalog, snapshot.Snapshot.RawJson);
        Assert.Equal(snapshot.Checksum, snapshot.Snapshot.Checksum);
        Assert.Equal("models.dev", snapshot.Snapshot.Source);
    }

    [Fact]
    public async Task Invalid_catalog_json_is_rejected_without_publishing_a_snapshot()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Json("not-json")));
        using var http = new HttpClient(handler);
        var source = new ModelsDevCatalogSource(http, new ModelsDevCatalogOptions
        {
            Url = "http://localhost/api.json"
        });

        var error = await Assert.ThrowsAsync<ModelCatalogSourceException>(() =>
            source.FetchAsync(new DateOnly(2026, 11, 24), CancellationToken.None));

        Assert.Contains("invalid JSON", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public async Task Remote_catalog_requires_https_unless_loopback()
    {
        var source = new ModelsDevCatalogSource(new HttpClient(), new ModelsDevCatalogOptions
        {
            Url = "http://catalog.example.test/api.json"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.FetchAsync(new DateOnly(2026, 11, 24), CancellationToken.None));
    }
}
