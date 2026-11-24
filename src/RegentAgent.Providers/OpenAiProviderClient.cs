using System.Text.Json.Nodes;

namespace RegentAgent.Providers;

public sealed class OpenAiProviderClient(HttpClient http, ProviderEndpointOptions options)
    : HttpProviderClient(http, options)
{
    protected override string ProviderName => "openai";
    protected override string ProtocolName => "openai_chat_completions";
    protected override string EndpointPath => "chat/completions";

    protected override void ConfigureHeaders(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    protected override ProviderResponse ParseResponse(string json, long latencyMs)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new ProviderRequestException(null, "PROVIDER_RESPONSE_INVALID", false, "OpenAI response is invalid.");
        var text = root["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
        var input = root["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0;
        var output = root["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0;
        var cached = root["usage"]?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0;
        var reasoning = root["usage"]?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0;
        var id = root["id"]?.GetValue<string>() ?? $"openai-{Guid.NewGuid():N}";
        return new ProviderResponse(id, text, input, output, latencyMs, json)
        {
            CachedInputTokens = cached, ReasoningTokens = reasoning
        };
    }
}
