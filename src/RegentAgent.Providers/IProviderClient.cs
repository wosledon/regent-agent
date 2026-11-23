namespace RegentAgent.Providers;

public sealed record ProviderRequest(
    string Provider,
    string Model,
    string Protocol,
    string Prompt,
    int MaxOutputTokens,
    string? ApiKey);

public sealed record ProviderResponse(
    string ProviderRequestId,
    string Text,
    int InputTokens,
    int OutputTokens,
    long LatencyMs);

public interface IProviderClient
{
    string Provider { get; }
    bool CanHandle(string provider, string protocol);
    Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
}
