using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class IdempotencyService(RegentDbContext db, TimeProvider clock)
{
    public async Task<T?> GetCompletedAsync<T>(Guid tenantId, string key, string operation,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Key == key && x.Operation == operation && x.Status == "Completed", cancellationToken);
        return record is null ? default : JsonSerializer.Deserialize<T>(record.ResponseJson);
    }

    public async Task SaveCompletedAsync(Guid tenantId, string key, string operation, object response,
        CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Key == key && x.Operation == operation, cancellationToken);
        if (record is null)
        {
            record = new IdempotencyRecord { Id = Guid.NewGuid(), TenantId = tenantId, Key = key, Operation = operation };
            db.IdempotencyRecords.Add(record);
        }
        record.ResponseJson = JsonSerializer.Serialize(response);
        record.Status = "Completed";
        record.CreatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }
}
