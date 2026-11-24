using System.Text.Json.Nodes;

namespace RegentAgent.Providers;

public sealed class AnthropicProviderClient(HttpClient http, ProviderEndpointOptions options)
    : HttpProviderClient(http, options)
{
    protected override string ProviderName => "anthropic";
    protected override string ProtocolName => "anthropic_messages";
    protected override string EndpointPath => "messages";

    protected override void ConfigureHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
    }

    protected override ProviderResponse ParseResponse(string json, long latencyMs)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new ProviderRequestException(null, "PROVIDER_RESPONSE_INVALID", false, "Anthropic response is invalid.");
        var text = root["content"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;
        var input = root["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
        var output = root["usage"]?["output_tokens"]?.GetValue<int>() ?? 0;
        var cached = (root["usage"]?["cache_read_input_tokens"]?.GetValue<int>() ?? 0)
            + (root["usage"]?["cache_creation_input_tokens"]?.GetValue<int>() ?? 0);
        var id = root["id"]?.GetValue<string>() ?? $"anthropic-{Guid.NewGuid():N}";
        return new ProviderResponse(id, text, input, output, latencyMs, json)
        {
            CachedInputTokens = cached
        };
    }
}
