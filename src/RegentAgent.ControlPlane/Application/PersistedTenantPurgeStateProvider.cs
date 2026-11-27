using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class PersistedTenantPurgeStateProvider(IServiceScopeFactory scopeFactory, string lifecycleStatePath)
    : ITenantPurgeStateProvider
{
    private readonly string lifecycleStatePath = Path.GetFullPath(lifecycleStatePath);

    public async Task<bool> IsPurgeActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        return await TenantPurgeStateInspector.IsPurgeActiveAsync(db, tenantId, lifecycleStatePath, cancellationToken);
    }
}
