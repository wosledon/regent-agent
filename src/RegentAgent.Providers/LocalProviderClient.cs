using System.Text;

namespace RegentAgent.Providers;

public sealed class LocalProviderClient : IProviderClient
{
    public string Provider => "local";

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
