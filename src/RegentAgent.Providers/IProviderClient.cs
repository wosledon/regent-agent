namespace RegentAgent.Providers;

public sealed record ProviderRequest(
    string Provider,
    string Model,
    string Protocol,
    string Prompt,
    int MaxOutputTokens,
    string? ApiKey,
    string? NativeRequestJson = null,
    Func<ProviderAttemptRecord, CancellationToken, Task>? AttemptObserver = null);

public sealed record ProviderAttemptRecord(
    int Attempt,
    string ProviderRequestId,
    int InputTokens,
    int CachedInputTokens,
    int OutputTokens,
    int ReasoningTokens,
    bool Succeeded,
    string? ErrorCode = null);

public sealed record ProviderResponse(
    string ProviderRequestId,
    string Text,
    int InputTokens,
    int OutputTokens,
    long LatencyMs,
    string? NativeResponseJson = null)
{
    public int CachedInputTokens { get; init; }
    public int ReasoningTokens { get; init; }
}

public sealed record ProviderStreamChunk(string Data, string? EventType = null);

public interface IProviderClient
{
    string Provider { get; }
    bool CanHandle(string provider, string protocol);
    Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ProviderStreamChunk> StreamAsync(ProviderRequest request, CancellationToken cancellationToken) =>
        throw new ProviderRequestException(null, "PROVIDER_STREAM_UNSUPPORTED", false, "Streaming is not supported by this adapter.");
}
