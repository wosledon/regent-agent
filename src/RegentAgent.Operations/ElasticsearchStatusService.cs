using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Operations;
using RegentAgent.Observability;

namespace RegentAgent.Operations;

public sealed class ElasticsearchStatusService(
    IHttpClientFactory httpClientFactory,
    ObservabilityOptions options,
    TimeProvider clock)
{
    public async Task<ElasticsearchStatusDto> GetAsync(CancellationToken cancellationToken)
    {
        if (!options.Profile.Equals("elastic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.ElasticsearchEndpoint))
        {
            return new ElasticsearchStatusDto(false, false, null, null, null, null, null, [],
                clock.GetUtcNow(), "Elasticsearch profile is not configured.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var activity = RegentActivitySource.Instance.StartActivity("elasticsearch.status");
        try
        {
            using var client = httpClientFactory.CreateClient("elasticsearch-status");
            client.Timeout = TimeSpan.FromSeconds(5);
            if (!string.IsNullOrWhiteSpace(options.ElasticsearchUsername)
                && !string.IsNullOrWhiteSpace(options.ElasticsearchPassword))
            {
                var credentials = Encoding.UTF8.GetBytes($"{options.ElasticsearchUsername}:{options.ElasticsearchPassword}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));
            }

            using var rootResponse = await client.GetAsync(new Uri(options.ElasticsearchEndpoint), cancellationToken);
            rootResponse.EnsureSuccessStatusCode();
            using var root = JsonDocument.Parse(await rootResponse.Content.ReadAsStringAsync(cancellationToken));
            using var healthResponse = await client.GetAsync(new Uri(new Uri(options.ElasticsearchEndpoint.TrimEnd('/') + "/"), "_cluster/health"), cancellationToken);
            healthResponse.EnsureSuccessStatusCode();
            using var health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync(cancellationToken));
            var indices = await ReadIndicesAsync(client, options.ElasticsearchEndpoint, cancellationToken);

            activity?.SetTag("elasticsearch.cluster", root.RootElement.TryGetProperty("cluster_name", out var activityCluster) ? activityCluster.GetString() : null);
            activity?.SetTag("elasticsearch.status", health.RootElement.TryGetProperty("status", out var activityStatus) ? activityStatus.GetString() : null);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return new ElasticsearchStatusDto(true, true,
                root.RootElement.TryGetProperty("cluster_name", out var clusterName) ? clusterName.GetString() : null,
                health.RootElement.TryGetProperty("status", out var status) ? status.GetString() : null,
                root.RootElement.TryGetProperty("version", out var version) && version.TryGetProperty("number", out var number) ? number.GetString() : null,
                health.RootElement.TryGetProperty("number_of_nodes", out var nodes) && nodes.TryGetInt32(out var nodeCount) ? nodeCount : null,
                health.RootElement.TryGetProperty("active_primary_shards", out var shards) && shards.TryGetInt32(out var shardCount) ? shardCount : null,
                indices, clock.GetUtcNow());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            return new ElasticsearchStatusDto(true, false, null, null, null, null, null, [],
                clock.GetUtcNow(), $"Elasticsearch health check failed after {stopwatch.ElapsedMilliseconds} ms.");
        }
    }

    private static async Task<IReadOnlyList<ElasticsearchIndexDto>> ReadIndicesAsync(
        HttpClient client, string endpoint, CancellationToken cancellationToken)
    {
        var uri = new Uri(new Uri(endpoint.TrimEnd('/') + "/"), "_cat/indices?format=json&h=index,docs.count,store.size&expand_wildcards=open");
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.EnumerateArray()
            .Select(item => new { Item = item, Name = item.TryGetProperty("index", out var index) ? index.GetString() ?? string.Empty : string.Empty })
            .Where(item => item.Name.StartsWith(".ds-", StringComparison.Ordinal) || !item.Name.StartsWith('.'))
            .Select(item => new ElasticsearchIndexDto(
                item.Name,
                item.Item.TryGetProperty("docs.count", out var count) && long.TryParse(count.GetString(), out var documents) ? documents : null,
                item.Item.TryGetProperty("store.size", out var size) ? size.GetString() : null))
            .OrderByDescending(item => item.Documents ?? 0)
            .Take(50)
            .ToArray();
    }
}
