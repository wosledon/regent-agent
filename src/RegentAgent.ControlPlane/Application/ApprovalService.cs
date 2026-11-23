using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Results;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class ApprovalService(RegentDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<ApprovalDto?> GetAsync(ApprovalId id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var approval = await db.Approvals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        return approval is null ? null : Mappings.ToDto(approval);
    }

    public async Task<Result<ApprovalDto>> DecideAsync(ApprovalId id, ApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var approval = await db.Approvals.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        if (approval is null)
        {
            return Result.Failure<ApprovalDto>(Error.NotFound());
        }
        if (approval.State != ApprovalState.Pending)
        {
            return Result.Failure<ApprovalDto>(Error.Conflict("Approval is not pending."));
        }
        if (approval.ExpiresAt <= clock.GetUtcNow())
        {
            approval.State = ApprovalState.Expired;
            await db.SaveChangesAsync(cancellationToken);
            return Result.Failure<ApprovalDto>(Error.Conflict("Approval has expired."));
        }

        var normalized = decision.Decision.Trim().ToLowerInvariant();
        if (normalized is not ("approve" or "reject"))
        {
            return Result.Failure<ApprovalDto>(Error.Validation("Decision must be approve or reject."));
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        approval.State = normalized == "approve" ? ApprovalState.Approved : ApprovalState.Rejected;
        var seq = (await db.AuditRecords.Where(x => x.TenantId == tenantId).MaxAsync(x => (long?)x.Seq, cancellationToken) ?? 0) + 1;
        db.AuditRecords.Add(new AuditRecord
        {
            TenantId = tenantId,
            Seq = seq,
            EventType = "approval.decided",
            Actor = tenant.DisplayName,
            PayloadJson = JsonSerializer.Serialize(new { approval_id = approval.Id, decision = normalized, reason = decision.Reason }),
            CreatedAt = clock.GetUtcNow()
        });
        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientId = tenant.UserId.Value,
            Level = approval.Severity.ToString(),
            Title = $"{approval.Title}: {approval.State}",
            CreatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success(Mappings.ToDto(approval));
    }
}
