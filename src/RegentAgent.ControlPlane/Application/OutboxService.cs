using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class OutboxService(RegentDbContext db, TimeProvider clock)
{
    public async Task<OutboxRecord> PublishAsync(Guid tenantId, string eventType, object payload,
        CancellationToken cancellationToken)
    {
        var record = new OutboxRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload), CreatedAt = clock.GetUtcNow()
        };
        db.Outbox.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<int> ProcessAsync(CancellationToken cancellationToken)
    {
        var batch = await db.Outbox.Where(x => x.Status == "Pending").Take(100).ToListAsync(cancellationToken);
        var pending = batch.OrderBy(x => x.CreatedAt).ToArray();
        foreach (var message in pending)
        {
            if (!await db.Inbox.AnyAsync(x => x.TenantId == message.TenantId && x.MessageId == message.Id, cancellationToken))
                db.Inbox.Add(new InboxRecord
                {
                    Id = Guid.NewGuid(), TenantId = message.TenantId, MessageId = message.Id,
                    EventType = message.EventType, PayloadJson = message.PayloadJson, CreatedAt = clock.GetUtcNow()
                });
            message.Status = "Processed";
            message.ProcessedAt = clock.GetUtcNow();
            message.Attempts++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return pending.Length;
    }

    public async Task<IReadOnlyList<OutboxRecord>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await db.Outbox.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken))
            .OrderByDescending(x => x.CreatedAt).ToArray();
}
