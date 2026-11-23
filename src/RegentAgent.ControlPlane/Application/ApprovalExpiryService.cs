using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class ApprovalExpiryService(RegentDbContext db, AuditIntegrityService audit, TimeProvider clock)
{
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken)
    {
        var pending = await db.Approvals.Where(x => x.State == ApprovalState.Pending).ToListAsync(cancellationToken);
        var due = pending.Where(x => x.ExpiresAt <= clock.GetUtcNow()).ToArray();
        foreach (var approval in due) approval.State = ApprovalState.Expired;
        foreach (var approval in due)
        {
            await audit.AppendAsync(approval.TenantId, "approval.expired", "system",
                new { approval_id = approval.Id, action_type = approval.ActionType, expired_at = approval.ExpiresAt }, cancellationToken);
            var recipient = await db.Memberships.AsNoTracking()
                .Where(x => x.TenantId == approval.TenantId && x.Role == "Owner")
                .Select(x => (Guid?)x.UserId).FirstOrDefaultAsync(cancellationToken);
            if (recipient is not null)
                db.Notifications.Add(new NotificationRecord
                {
                    Id = Guid.NewGuid(), TenantId = approval.TenantId, RecipientId = recipient.Value,
                    Level = approval.Severity.ToString(), Title = $"{approval.Title}: Expired", CreatedAt = clock.GetUtcNow()
                });
        }
        await db.SaveChangesAsync(cancellationToken);
        return due.Length;
    }
}
