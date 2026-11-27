using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantPurgeWriteInterceptor(ITenantPurgeStateProvider? purgeState = null)
    : ISaveChangesInterceptor, IDisposable
{
    private readonly List<IDisposable> admissions = [];

    public async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await AcquireAsync(eventData.Context, cancellationToken);
        return result;
    }

    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AcquireAsync(eventData.Context).GetAwaiter().GetResult();
        return result;
    }

    public async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Release();
        return await Task.FromResult(result);
    }

    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Release();
        return result;
    }

    public async ValueTask SaveChangesFailedAsync(DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Release();
        await Task.CompletedTask;
    }

    public void SaveChangesFailed(DbContextErrorEventData eventData) => Release();

    public void Dispose() => Release();

    private async Task AcquireAsync(DbContext? context, CancellationToken cancellationToken = default)
    {
        if (context is null) return;
        var tenantIds = context.ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => entry.Entity switch
            {
                TenantRecord tenant => tenant.Id,
                _ when entry.Metadata.FindProperty("TenantId") is { } tenantProperty
                    => (Guid?)tenantProperty.PropertyInfo?.GetValue(entry.Entity),
                _ => null
            })
            .Where(id => id.HasValue && id != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        try
        {
            foreach (var tenantId in tenantIds)
            {
                if (TenantPurgeFence.HasWriteScope(tenantId)) continue;
                await TenantPurgeFence.CheckPersistedPurgeAsync(tenantId, cancellationToken, purgeState);
                admissions.Add(await TenantPurgeFence.EnterWriteAsync(tenantId, cancellationToken));
            }
        }
        catch
        {
            Release();
            throw;
        }
    }

    private void Release()
    {
        foreach (var admission in admissions) admission.Dispose();
        admissions.Clear();
    }
}
