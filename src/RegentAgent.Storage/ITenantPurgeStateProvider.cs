namespace RegentAgent.Storage;

public interface ITenantPurgeStateProvider
{
    Task<bool> IsPurgeActiveAsync(Guid tenantId, CancellationToken cancellationToken);
}
