using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

internal static class TenantPurgeStateInspector
{
    public const string DefaultLifecycleStatePath = "data/modules/lifecycle.json";

    public static async Task<bool> IsPurgeActiveAsync(RegentDbContext db, Guid tenantId,
        string lifecycleStatePath, CancellationToken cancellationToken)
    {
        var status = await GetTenantStatusAsync(db, tenantId, cancellationToken);
        if (IsPurgeStatus(status)) return true;

        var fullPath = Path.GetFullPath(lifecycleStatePath);
        if (!File.Exists(fullPath)) return false;
        var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var states = JsonSerializer.Deserialize<Dictionary<string, DataLifecycleState>>(json)
            ?? new Dictionary<string, DataLifecycleState>(StringComparer.Ordinal);
        return states.Values.Any(state => HasPurgeState(state, tenantId));
    }

    public static async Task<bool> IsPurgeActiveAsync(RegentDbContext db,
        IDocumentStore<DataLifecycleState>? lifecycleStore, Guid tenantId)
    {
        var status = await GetTenantStatusAsync(db, tenantId, CancellationToken.None);
        if (IsPurgeStatus(status)) return true;
        if (lifecycleStore is null) return false;

        var state = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
        return HasPurgeState(state, tenantId);
    }

    private static Task<string?> GetTenantStatusAsync(RegentDbContext db, Guid tenantId,
        CancellationToken cancellationToken) => db.Tenants.AsNoTracking()
        .Where(tenant => tenant.Id == tenantId)
        .Select(tenant => tenant.Status)
        .SingleOrDefaultAsync(cancellationToken);

    private static bool HasPurgeState(DataLifecycleState? state, Guid tenantId) =>
        state is not null && state.TenantId == tenantId && (state.IsPurging || state.IsPurged);

    private static bool IsPurgeStatus(string? status) =>
        status is not null && (status.Equals("Purging", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Purged", StringComparison.OrdinalIgnoreCase));
}
