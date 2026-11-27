using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class OutboxService(RegentDbContext db, TimeProvider clock,
    IEnumerable<IOutboxEventHandler>? handlers = null, ILogger<OutboxService>? logger = null)
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim InMemoryGate = new(1, 1);
    private readonly IReadOnlyDictionary<string, IOutboxEventHandler> handlersByType =
        (handlers ?? []).ToDictionary(x => x.EventType, StringComparer.Ordinal);

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
        var now = clock.GetUtcNow();
        var batch = await db.Outbox.AsNoTracking()
            .Where(x => (x.Status == "Pending" && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                || (x.Status == "Processing" && x.NextAttemptAt != null && x.NextAttemptAt <= now))
            .OrderBy(x => x.CreatedAt).Take(100).ToListAsync(cancellationToken);
        foreach (var candidate in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryClaimAsync(candidate.Id, now, cancellationToken)) continue;
            var message = await db.Outbox.SingleAsync(x => x.Id == candidate.Id, cancellationToken);
            if (await db.Inbox.AnyAsync(x => x.TenantId == message.TenantId && x.MessageId == message.Id,
                    cancellationToken))
            {
                await MarkProcessedAsync(message, cancellationToken);
                continue;
            }

            try
            {
                if (handlersByType.TryGetValue(message.EventType, out var handler))
                    await handler.HandleAsync(message, cancellationToken);
                db.Inbox.Add(new InboxRecord
                {
                    Id = Guid.NewGuid(), TenantId = message.TenantId, MessageId = message.Id,
                    EventType = message.EventType, PayloadJson = message.PayloadJson, CreatedAt = clock.GetUtcNow()
                });
                await MarkProcessedAsync(message, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.LastError = exception.GetType().Name;
                if (message.Attempts >= MaximumAttempts)
                {
                    message.Status = "Quarantined";
                    message.NextAttemptAt = null;
                }
                else
                {
                    message.Status = "Pending";
                    message.NextAttemptAt = clock.GetUtcNow().Add(Backoff(message));
                }
                await db.SaveChangesAsync(cancellationToken);
                logger?.LogWarning(exception,
                    "Outbox message {MessageId} for tenant {TenantId} failed on attempt {Attempts} with status {Status}.",
                    message.Id, message.TenantId, message.Attempts, message.Status);
            }
        }
        return batch.Count;
    }

    private async Task<bool> TryClaimAsync(Guid messageId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var claimed = await db.Outbox
                .Where(x => x.Id == messageId
                    && ((x.Status == "Pending" && (x.NextAttemptAt == null || x.NextAttemptAt <= now))
                        || (x.Status == "Processing" && x.NextAttemptAt != null && x.NextAttemptAt <= now)))
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Processing")
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.NextAttemptAt, now + ProcessingLease)
                    .SetProperty(x => x.LastError, (string?)null), cancellationToken);
            return claimed == 1;
        }

        await InMemoryGate.WaitAsync(cancellationToken);
        try
        {
            var message = await db.Outbox.SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
            var due = message is not null && ((message.Status == "Pending"
                        && (message.NextAttemptAt is null || message.NextAttemptAt <= now))
                    || (message.Status == "Processing" && message.NextAttemptAt is not null
                        && message.NextAttemptAt <= now));
            if (!due) return false;
            message!.Status = "Processing";
            message.Attempts++;
            message.NextAttemptAt = now + ProcessingLease;
            message.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally { InMemoryGate.Release(); }
    }

    private async Task MarkProcessedAsync(OutboxRecord message, CancellationToken cancellationToken)
    {
        message.Status = "Processed";
        message.ProcessedAt = clock.GetUtcNow();
        message.NextAttemptAt = null;
        message.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static TimeSpan Backoff(OutboxRecord message)
    {
        var exponent = Math.Clamp(message.Attempts - 1, 0, 4);
        var seconds = Math.Min(300, 30 * (1 << exponent));
        var jitter = BitConverter.ToUInt32(message.Id.ToByteArray(), 0) % (uint)(seconds / 2 + 1);
        return TimeSpan.FromSeconds(seconds + jitter);
    }

    public async Task<IReadOnlyList<OutboxRecord>> ListAsync(Guid tenantId, CancellationToken cancellationToken) =>
        (await db.Outbox.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken))
        .OrderByDescending(x => x.CreatedAt).ToArray();
}
