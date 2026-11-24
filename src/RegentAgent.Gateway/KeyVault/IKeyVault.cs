namespace RegentAgent.Gateway.KeyVault;

public sealed record KeyVersionInfo(
    string KeyRef,
    string TenantId,
    string Provider,
    string Model,
    int Version,
    string State,
    DateTimeOffset CreatedAt);

public interface IKeyVault
{
    Task<KeyVersionInfo> SetAsync(string tenantId, string provider, string model, string apiKey, CancellationToken cancellationToken);
    Task<KeyVersionInfo> RotateAsync(string tenantId, string keyRef, string apiKey, CancellationToken cancellationToken);
    Task<string?> UnlockAsync(string tenantId, string keyRef, CancellationToken cancellationToken);
    Task DeleteAsync(string tenantId, string keyRef, CancellationToken cancellationToken);
    Task<IReadOnlyList<KeyVersionInfo>> ListAsync(string tenantId, CancellationToken cancellationToken);
}
