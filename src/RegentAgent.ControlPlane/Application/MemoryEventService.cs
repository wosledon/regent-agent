using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class MemoryEventService(RegentDbContext db, AuditIntegrityService audit,
    OutboxService outbox, TimeProvider clock)
{
    public async Task RecordAsync(Guid tenantId, Guid? recipientId, string actor, string eventType,
        object payload, string? notificationTitle = null, string notificationLevel = "Medium",
        CancellationToken cancellationToken = default)
    {
        await audit.AppendAsync(tenantId, eventType, actor, payload, cancellationToken);
        await outbox.PublishAsync(tenantId, eventType, payload, cancellationToken);
        if (string.IsNullOrWhiteSpace(notificationTitle)) return;
        var recipient = recipientId ?? await db.Memberships.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Role == "Owner")
            .Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(cancellationToken);
        if (recipient is null) return;
        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientId = recipient.Value,
            Level = notificationLevel, Title = notificationTitle, CreatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
