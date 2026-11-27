using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class IdempotencyService(RegentDbContext db, TimeProvider clock)
{
    public async Task<T?> GetCompletedAsync<T>(Guid tenantId, string key, string operation,
        CancellationToken cancellationToken, string? requestHash = null)
    {
        var record = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Key == key && x.Operation == operation && x.Status == "Completed", cancellationToken);
        if (record is not null && requestHash is not null && !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException();
        return record is null ? default : JsonSerializer.Deserialize<T>(record.ResponseJson);
    }

    public async Task SaveCompletedAsync(Guid tenantId, string key, string operation, object response,
        CancellationToken cancellationToken, string? requestHash = null, bool saveChanges = true)
    {
        var record = await db.IdempotencyRecords.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Key == key && x.Operation == operation, cancellationToken);
        if (record is null)
        {
            record = new IdempotencyRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Key = key, Operation = operation,
                CreatedAt = clock.GetUtcNow()
            };
            db.IdempotencyRecords.Add(record);
        }
        else
        {
            if (requestHash is not null && !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
                throw new IdempotencyConflictException();
            if (record.Status == "Completed") return;
            if (record.Status is not ("InProgress" or "Failed"))
                throw new IdempotencyConflictException();
        }

        record.ResponseJson = JsonSerializer.Serialize(response);
        if (requestHash is not null) record.RequestHash = requestHash;
        record.Status = "Completed";
        if (saveChanges) await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed class IdempotencyConflictException : Exception
{
}
