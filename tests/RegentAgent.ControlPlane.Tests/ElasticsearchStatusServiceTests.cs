using System.Net;
using System.Text;
using Moq;
using RegentAgent.Observability;
using RegentAgent.Operations;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ElasticsearchStatusServiceTests
{
    [Fact]
    public async Task Local_profile_reports_elasticsearch_as_not_configured()
    {
        var http = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        var service = new ElasticsearchStatusService(http.Object, new ObservabilityOptions(), TimeProvider.System);

        var status = await service.GetAsync(CancellationToken.None);

        Assert.False(status.Configured);
        Assert.False(status.Reachable);
        Assert.Empty(status.Indices);
        http.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Elastic_profile_reads_cluster_health_and_indices_with_authentication()
    {
        var handler = new ElasticsearchHandler();
        var http = new Mock<IHttpClientFactory>();
        http.Setup(factory => factory.CreateClient("elasticsearch-status")).Returns(new HttpClient(handler));
        var service = new ElasticsearchStatusService(http.Object, ElasticOptions(), TimeProvider.System);

        var status = await service.GetAsync(CancellationToken.None);

        Assert.True(status.Configured);
        Assert.True(status.Reachable);
        Assert.Equal("docker-cluster", status.ClusterName);
        Assert.Equal("green", status.Status);
        Assert.Equal("9.5.5", status.Version);
        Assert.Equal(1, status.Nodes);
        Assert.Equal(3, status.ActivePrimaryShards);
        Assert.Equal("logs-regent-default", Assert.Single(status.Indices).Name);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.False(string.IsNullOrWhiteSpace(handler.AuthorizationParameter));
    }

    [Fact]
    public async Task Elasticsearch_failure_returns_sanitized_unavailable_status()
    {
        var http = new Mock<IHttpClientFactory>();
        http.Setup(factory => factory.CreateClient("elasticsearch-status"))
            .Returns(new HttpClient(new ElasticsearchHandler(fail: true)));
        var service = new ElasticsearchStatusService(http.Object, ElasticOptions(), TimeProvider.System);

        var status = await service.GetAsync(CancellationToken.None);

        Assert.True(status.Configured);
        Assert.False(status.Reachable);
        Assert.Contains("health check failed", status.Error);
        Assert.DoesNotContain("sensitive connection detail", status.Error);
    }

    private static ObservabilityOptions ElasticOptions() => new()
    {
        Profile = "elastic",
        OtlpEndpoint = "http://elasticsearch:9200/_otlp",
        ElasticsearchEndpoint = "http://elasticsearch:9200",
        ElasticsearchUsername = "elastic",
        ElasticsearchPassword = "test-password"
    };

    private sealed class ElasticsearchHandler(bool fail = false) : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            if (fail) throw new HttpRequestException("sensitive connection detail");
            var path = request.RequestUri!.AbsolutePath;
            var json = path switch
            {
                "/" => """{"cluster_name":"docker-cluster","version":{"number":"9.5.5"}}""",
                "/_cluster/health" => """{"status":"green","number_of_nodes":1,"active_primary_shards":3}""",
                _ => """[{"index":"logs-regent-default","docs.count":"12","store.size":"24kb"},{"index":".hidden","docs.count":"1","store.size":"1kb"}]"""
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
