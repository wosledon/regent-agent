namespace RegentAgent.Gateway.Metering;

public sealed class LocalBudgetService(long monthlyLimit)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private long committed;

    public async Task<bool> TryReserveAsync(long amount, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (committed + amount > monthlyLimit) return false;
            committed += amount;
            return true;
        }
        finally { gate.Release(); }
    }

    public Task<long> GetCommittedAsync() => Task.FromResult(committed);
}
