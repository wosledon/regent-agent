using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RegentAgent.Contracts.Models;

namespace RegentAgent.Providers;

public sealed class ModelsDevCatalogOptions
{
    public string Url { get; set; } = "https://models.dev/api.json";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;
    public int MaxRetries { get; set; } = 2;
    public int BaseDelayMilliseconds { get; set; } = 200;

    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("models.dev URL is invalid.");
        var loopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && loopback))
            throw new InvalidOperationException("models.dev URL must use HTTPS or loopback HTTP for tests.");
        if (TimeoutSeconds is < 1 or > 300 || MaxResponseBytes is < 1024 or > 64 * 1024 * 1024
            || MaxRetries is < 0 or > 5 || BaseDelayMilliseconds is < 0 or > 10_000)
            throw new InvalidOperationException("models.dev HTTP limits are invalid.");
    }
}

public sealed class ModelsDevCatalogSource(HttpClient http, ModelsDevCatalogOptions options, TimeProvider? clock = null)
    : IModelCatalogSource
{
    private readonly HttpClient http = http;
    private readonly ModelsDevCatalogOptions options = options;
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    public const string SourceName = "models.dev";
    public string SourceId => SourceName;

    public async Task<ModelCatalogSnapshot> FetchAsync(DateOnly snapshotDate, CancellationToken cancellationToken)
    {
        options.Validate();
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryDelay = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                string raw;
                try { raw = await ProviderHttpContent.ReadBoundedAsync(response, options.MaxResponseBytes, cancellationToken); }
                catch (ProviderRequestException exception)
                {
                    throw new ModelCatalogSourceException(exception.Message, exception.StatusCode, exception.Retryable, exception);
                }
                if (!response.IsSuccessStatusCode)
                {
                    var retryable = ProviderHttpRetry.IsRetryable(response.StatusCode);
                    if (!retryable || attempt >= options.MaxRetries)
                        throw new ModelCatalogSourceException($"models.dev returned HTTP {(int)response.StatusCode}.",
                            (int)response.StatusCode, retryable);
                    retryDelay = ProviderHttpRetry.Delay(response, attempt, options.BaseDelayMilliseconds);
                    await Task.Delay(retryDelay.Value, cancellationToken);
                    continue;
                }
                JsonNode root;
                try { root = JsonNode.Parse(raw) ?? throw new JsonException("Empty catalog."); }
                catch (JsonException exception) { throw new ModelCatalogSourceException("models.dev returned invalid JSON.", innerException: exception); }
                var entries = ParseEntries(root, snapshotDate).ToArray();
                if (entries.Length == 0) throw new ModelCatalogSourceException("models.dev catalog contains no supported OpenAI or Anthropic models.");
                var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
                var fetchedAt = clock.GetUtcNow();
                var snapshot = new ModelSnapshot($"models-dev-{snapshotDate:yyyyMMdd}", snapshotDate, SourceId, true)
                {
                    RawJson = raw, Checksum = checksum, FetchedAt = fetchedAt
                };
                return new ModelCatalogSnapshot(snapshot, entries, raw, checksum, fetchedAt);
            }
            catch (ModelCatalogSourceException) { throw; }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= options.MaxRetries)
                    throw new ModelCatalogSourceException("models.dev request timed out.", retryable: true);
            }
            catch (HttpRequestException)
            {
                if (attempt >= options.MaxRetries)
                    throw new ModelCatalogSourceException("models.dev is unavailable.", retryable: true);
            }
            await Task.Delay(retryDelay ?? ProviderHttpRetry.Delay(attempt, options.BaseDelayMilliseconds), cancellationToken);
        }
    }

    private static IEnumerable<ModelCatalogEntry> ParseEntries(JsonNode root, DateOnly snapshotDate)
    {
        if (root is not JsonObject providers) yield break;
        foreach (var provider in providers)
        {
            var providerId = provider.Key.ToLowerInvariant();
            var protocol = providerId switch { "openai" => "OpenAI", "anthropic" => "Anthropic", _ => null };
            if (protocol is null || provider.Value?["models"] is not JsonObject models) continue;
            foreach (var model in models)
            {
                if (model.Value is not JsonObject value) continue;
                var modelId = value["id"]?.GetValue<string>() ?? model.Key;
                if (string.IsNullOrWhiteSpace(modelId)) continue;
                var version = value["version"]?.GetValue<string>()
                    ?? value["last_updated"]?.GetValue<string>() ?? snapshotDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                var cost = value["cost"];
                var input = ReadDecimal(cost?["input"]);
                var output = ReadDecimal(cost?["output"]);
                yield return new ModelCatalogEntry
                {
                    Provider = protocol, ModelId = modelId, Version = version, Protocol = protocol,
                    PriceState = input.HasValue && output.HasValue ? "Priced" : "Unpriced",
                    InputPricePerMillion = input, OutputPricePerMillion = output,
                    Currency = cost?["currency"]?.GetValue<string>() ?? "USD",
                    Tier = "Unassigned", Source = SourceName
                };
            }
        }
    }

    private static decimal? ReadDecimal(JsonNode? node)
    {
        if (node is null) return null;
        return decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
