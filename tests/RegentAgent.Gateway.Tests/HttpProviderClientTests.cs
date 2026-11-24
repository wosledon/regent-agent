using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using RegentAgent.Providers;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class HttpProviderClientTests
{
    [Fact]
    public async Task OpenAi_adapter_forwards_native_json_and_returns_native_response()
    {
        var responseJson = """{"id":"chatcmpl-1","choices":[{"message":{"role":"assistant","content":"native"}}],"usage":{"prompt_tokens":7,"completion_tokens":2}}""";
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json(responseJson)));
        using var http = new HttpClient(handler);
        var client = new OpenAiProviderClient(http, new ProviderEndpointOptions
        {
            BaseUrl = "http://localhost/v1", MaxRetries = 0
        });
        var native = """{"model":"alias","messages":[{"role":"user","content":"hello"}],"tools":[{"type":"function","function":{"name":"search"}}],"temperature":0.2}""";

        var response = await client.CompleteAsync(new ProviderRequest("openai", "gpt-test", "openai_chat_completions",
            "hello", 128, "secret", native), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/chat/completions", request.Uri.AbsolutePath);
        Assert.Equal("Bearer secret", request.Headers["Authorization"]);
        var payload = JsonNode.Parse(request.Body!)!;
        Assert.Equal("gpt-test", payload["model"]!.GetValue<string>());
        Assert.NotNull(payload["tools"]);
        Assert.Equal(0.2, payload["temperature"]!.GetValue<double>());
        Assert.Equal(responseJson, response.NativeResponseJson);
        Assert.Equal(7, response.InputTokens);
        Assert.Equal(2, response.OutputTokens);
    }

    [Fact]
    public async Task Anthropic_adapter_preserves_system_and_uses_native_headers()
    {
        var responseJson = """{"id":"msg_1","content":[{"type":"text","text":"anthropic"}],"usage":{"input_tokens":9,"output_tokens":3}}""";
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json(responseJson)));
        using var http = new HttpClient(handler);
        var client = new AnthropicProviderClient(http, new ProviderEndpointOptions
        {
            BaseUrl = "http://localhost/v1", MaxRetries = 0
        });
        var native = """{"model":"alias","system":"policy","messages":[{"role":"user","content":"hello"}],"max_tokens":64}""";

        var response = await client.CompleteAsync(new ProviderRequest("anthropic", "claude-test", "anthropic_messages",
            "hello", 64, "secret", native), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/messages", request.Uri.AbsolutePath);
        Assert.Equal("secret", request.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", request.Headers["anthropic-version"]);
        Assert.Equal("policy", JsonNode.Parse(request.Body!)!["system"]!.GetValue<string>());
        Assert.Equal(responseJson, response.NativeResponseJson);
    }

    [Fact]
    public async Task Retryable_provider_errors_are_retried_without_changing_payload()
    {
        var attempts = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? FakeHttpMessageHandler.Json("""{"error":"busy"}""", HttpStatusCode.ServiceUnavailable)
                : FakeHttpMessageHandler.Json("""{"id":"ok","choices":[{"message":{"content":"done"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}"""));
        });
        using var http = new HttpClient(handler);
        var client = new OpenAiProviderClient(http, new ProviderEndpointOptions
        {
            BaseUrl = "http://localhost/v1", MaxRetries = 1, BaseDelayMilliseconds = 0
        });

        var response = await client.CompleteAsync(new ProviderRequest("openai", "gpt-test", "openai_chat_completions",
            "hello", 32, "secret", """{"messages":[]}"""), CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        Assert.Equal("done", response.Text);
    }

    [Fact]
    public async Task Non_retryable_provider_errors_expose_stable_metadata()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(FakeHttpMessageHandler.Json("""{"error":"bad request"}""", HttpStatusCode.BadRequest)));
        using var http = new HttpClient(handler);
        var client = new OpenAiProviderClient(http, new ProviderEndpointOptions
        {
            BaseUrl = "http://localhost/v1", MaxRetries = 2, BaseDelayMilliseconds = 0
        });

        var error = await Assert.ThrowsAsync<ProviderRequestException>(() => client.CompleteAsync(
            new ProviderRequest("openai", "gpt-test", "openai_chat_completions", "hello", 32, "secret"),
            CancellationToken.None));

        Assert.Equal(400, error.StatusCode);
        Assert.False(error.Retryable);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Provider_router_selects_local_or_remote_by_configuration()
    {
        var local = new LocalProviderClient();
        var remote = new StubProviderClient("openai");
        var localRouter = new ProviderRouter([local, remote], new ProviderRoutingOptions { Mode = "local" });
        var remoteRouter = new ProviderRouter([local, remote], new ProviderRoutingOptions { Mode = "remote" });
        var request = new ProviderRequest("openai", "model", "openai_chat_completions", "hello", 32, null);

        Assert.Contains("Local deterministic", (await localRouter.CompleteAsync(request, CancellationToken.None)).Text);
        Assert.Equal("remote", (await remoteRouter.CompleteAsync(request, CancellationToken.None)).Text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ProviderRouter([local],
            new ProviderRoutingOptions { Mode = "remote" }).CompleteAsync(request, CancellationToken.None));
    }

    private sealed class StubProviderClient(string provider) : IProviderClient
    {
        public string Provider => provider;
        public bool CanHandle(string candidate, string protocol) => candidate == provider && protocol == "openai_chat_completions";
        public Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderResponse("stub", "remote", 1, 1, 1));
    }
}
