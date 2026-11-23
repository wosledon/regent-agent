using RegentAgent.Providers;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class LocalProviderClientTests
{
    [Fact]
    public async Task Local_provider_is_deterministic_and_protocol_aware()
    {
        var client = new LocalProviderClient();
        var request = new ProviderRequest("openai", "local-cheap", "openai_chat_completions", "Summarize project status", 128, null);
        var first = await client.CompleteAsync(request, CancellationToken.None);
        var second = await client.CompleteAsync(request, CancellationToken.None);

        Assert.Contains("Summarize project status", first.Text);
        Assert.Equal(first.Text, second.Text);
        Assert.True(first.InputTokens > 0);
        Assert.True(first.OutputTokens > 0);
        Assert.True(client.CanHandle("anthropic", "anthropic_messages"));
        Assert.False(client.CanHandle("openai", "anthropic_messages"));
    }
}
