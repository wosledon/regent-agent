using System.Collections.Concurrent;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantObjectUploadTracker
{
    private readonly ConcurrentDictionary<Guid, int> pending = new();

    public int GetPendingCount(Guid tenantId) => pending.TryGetValue(tenantId, out var count) ? count : 0;

    public IDisposable Track(Guid tenantId)
    {
        pending.AddOrUpdate(tenantId, 1, (_, count) => count + 1);
        return new UploadLease(this, tenantId);
    }

    public async Task WaitForDrainAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        while (GetPendingCount(tenantId) > 0)
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Complete(Guid tenantId)
    {
        if (pending.TryGetValue(tenantId, out var count))
        {
            if (count <= 1) pending.TryRemove(tenantId, out _);
            else pending[tenantId] = count - 1;
        }
    }

    private sealed class UploadLease(TenantObjectUploadTracker owner, Guid tenantId) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.Complete(tenantId);
        }
    }
}
