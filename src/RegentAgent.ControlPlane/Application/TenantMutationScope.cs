using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

internal sealed class TenantMutationScope : IAsyncDisposable
{
    private static readonly SemaphoreSlim InMemoryGate = new(1, 1);
    private readonly IDbContextTransaction? transaction;
    private readonly bool gateAcquired;

    private TenantMutationScope(IDbContextTransaction? transaction, bool gateAcquired)
    {
        this.transaction = transaction;
        this.gateAcquired = gateAcquired;
    }

    public static async Task<TenantMutationScope> BeginAsync(RegentDbContext db, Guid tenantId,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        var gateAcquired = false;
        try
        {
            if (db.Database.IsRelational())
            {
                if (db.Database.CurrentTransaction is null)
                    transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                if (db.Database.IsNpgsql())
                {
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT pg_advisory_xact_lock(hashtext({tenantId.ToString("D")}))", cancellationToken);
                }
                else
                {
                    await db.Tenants.Where(x => x.Id == tenantId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, x => x.Name),
                            cancellationToken);
                }
            }
            else
            {
                await InMemoryGate.WaitAsync(cancellationToken);
                gateAcquired = true;
            }
            return new TenantMutationScope(transaction, gateAcquired);
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (gateAcquired) InMemoryGate.Release();
            throw;
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        if (gateAcquired) InMemoryGate.Release();
    }
}
