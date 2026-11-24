using System.Collections.Concurrent;

namespace RegentAgent.Gateway.Metering;

public sealed class LocalBudgetService(long monthlyLimit)
{
    private readonly ConcurrentDictionary<string, long> committedByTenant = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<bool> TryReserveAsync(string tenantId, long amount, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var committed = committedByTenant.GetValueOrDefault(tenantId);
            if (committed + amount > monthlyLimit) return false;
            committedByTenant[tenantId] = committed + amount;
            return true;
        }
        finally { gate.Release(); }
    }

    public Task<long> GetCommittedAsync(string tenantId) =>
        Task.FromResult(committedByTenant.GetValueOrDefault(tenantId));
}
