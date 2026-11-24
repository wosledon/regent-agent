namespace RegentAgent.Providers;

public sealed class ProviderRoutingOptions
{
    public string Mode { get; set; } = "local";
}

public interface IProviderRouter
{
    Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ProviderStreamChunk> StreamAsync(ProviderRequest request, CancellationToken cancellationToken);
}

public sealed class ProviderRouter(IEnumerable<IProviderClient> clients, ProviderRoutingOptions options) : IProviderRouter
{
    private readonly IProviderClient[] clients = clients.ToArray();
    private readonly ProviderRoutingOptions options = options;

    public async Task<ProviderResponse> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        return await SelectClient(request).CompleteAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ProviderStreamChunk> StreamAsync(ProviderRequest request, CancellationToken cancellationToken) =>
        SelectClient(request).StreamAsync(request, cancellationToken);

    private IProviderClient SelectClient(ProviderRequest request)
    {
        var mode = options.Mode.Trim().ToLowerInvariant();
        if (mode is not ("local" or "remote")) throw new InvalidOperationException("Provider mode must be local or remote.");
        var candidates = this.clients.Where(client => mode == "local"
            ? client.Provider.Equals("local", StringComparison.OrdinalIgnoreCase)
            : !client.Provider.Equals("local", StringComparison.OrdinalIgnoreCase));
        return candidates.FirstOrDefault(client => client.CanHandle(request.Provider, request.Protocol))
            ?? throw new InvalidOperationException($"No provider adapter handles {request.Provider}/{request.Protocol} in {mode} mode.");
    }
}
