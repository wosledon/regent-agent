using System.Text;
using System.Text.Json;

namespace RegentAgent.Providers;

public sealed class LocalProviderClient : IProviderClient
{
    public string Provider => "local";

    public async IAsyncEnumerable<ProviderStreamChunk> StreamAsync(ProviderRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanHandle(request.Provider, request.Protocol)) throw new InvalidOperationException("Provider protocol mismatch.");
        var prompt = request.Prompt.Trim();
        var summary = prompt.Length <= 160 ? prompt : prompt[..160] + "…";
        var text = $"Local deterministic response for {request.Model}: {summary}";
        var inputTokens = Math.Max(1, Encoding.UTF8.GetByteCount(prompt) / 4);
        var outputTokens = Math.Max(1, Encoding.UTF8.GetByteCount(text) / 4);
        var id = $"local-{Guid.NewGuid():N}";
        if (request.Protocol == "openai_chat_completions")
        {
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                id, @object = "chat.completion.chunk", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model = request.Model,
                choices = new[] { new { index = 0, delta = new { role = "assistant", content = text }, finish_reason = (string?)null } }
            }));
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                id, @object = "chat.completion.chunk", created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), model = request.Model,
                choices = Array.Empty<object>(), usage = new { prompt_tokens = inputTokens, completion_tokens = outputTokens, total_tokens = inputTokens + outputTokens }
            }));
            yield return new ProviderStreamChunk("[DONE]");
        }
        else
        {
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                type = "message_start", message = new
                {
                    id, type = "message", role = "assistant", model = request.Model, content = Array.Empty<object>(),
                    usage = new { input_tokens = inputTokens, output_tokens = 0 }
                }
            }), "message_start");
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                type = "content_block_start", index = 0, content_block = new { type = "text", text = string.Empty }
            }), "content_block_start");
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                type = "content_block_delta", index = 0, delta = new { type = "text_delta", text }
            }), "content_block_delta");
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                type = "content_block_stop", index = 0
            }), "content_block_stop");
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new
            {
                type = "message_delta", delta = new { stop_reason = "end_turn" },
                usage = new { output_tokens = outputTokens }
            }), "message_delta");
            yield return new ProviderStreamChunk(JsonSerializer.Serialize(new { type = "message_stop" }), "message_stop");
        }
        await Task.CompletedTask;
    }

    public bool CanHandle(string provider, string protocol) =>
        (provider == "openai" && protocol == "openai_chat_completions") ||
        (provider == "anthropic" && protocol == "anthropic_messages");

    public Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = request.Prompt.Trim();
        var summary = prompt.Length <= 160 ? prompt : prompt[..160] + "…";
        var text = $"Local deterministic response for {request.Model}: {summary}";
        var inputTokens = Math.Max(1, Encoding.UTF8.GetByteCount(prompt) / 4);
        var outputTokens = Math.Max(1, Encoding.UTF8.GetByteCount(text) / 4);
        return Task.FromResult(new ProviderResponse(
            $"local-{Guid.NewGuid():N}", text, inputTokens, outputTokens, 1));
    }
}
