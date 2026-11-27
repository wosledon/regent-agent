using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Results;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class ApprovalService(RegentDbContext db, ITenantContext tenant, TimeProvider clock,
    AuditIntegrityService audit, IdempotencyService idempotency, ApprovalAuthority authority)
{
    private static readonly SemaphoreSlim InMemoryGate = new(1, 1);

    public async Task<ApprovalDto> CreateAsync(CreateApprovalDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.ActionType))
            throw new InvalidOperationException("Approval title and action type are required.");
        if (input.ExpiresInMinutes <= 0) throw new InvalidOperationException("Approval expiry must be positive.");

        var tenantId = tenant.TenantId.Value;
        var requesterUserId = tenant.UserId.Value;
        if (requesterUserId == Guid.Empty) throw new InvalidOperationException("Authenticated requester is required.");
        var binding = new ApprovalBinding(tenantId, input.ActionType, input.TargetEmployeeId,
            input.TargetCandidateId, input.TargetPositionId);
        await ValidateBindingAsync(binding, cancellationToken);

        var relational = db.Database.IsRelational();
        await using var transaction = relational && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var gateAcquired = false;
        try
        {
            if (!relational)
            {
                await InMemoryGate.WaitAsync(cancellationToken);
                gateAcquired = true;
            }
            var now = clock.GetUtcNow();
            var record = new ApprovalRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ActionType = binding.NormalizedActionType,
                Title = input.Title, Impact = input.Impact, Severity = input.Severity,
                Requester = tenant.DisplayName, RequesterUserId = requesterUserId,
                TargetEmployeeId = binding.TargetEmployeeId, TargetCandidateId = binding.TargetCandidateId,
                TargetPositionId = binding.TargetPositionId, ActionHash = binding.ComputeV2Hash(),
                CreatedAt = now, ExpiresAt = now.AddMinutes(input.ExpiresInMinutes),
                OptionsJson = JsonSerializer.Serialize(new[] { "Approve", "Reject" }),
                EvidenceJson = JsonSerializer.Serialize(input.EvidenceRefs)
            };
            db.Approvals.Add(record);
            await audit.AppendAsync(tenantId, "approval.created", tenant.DisplayName, new
            {
                approval_id = record.Id, action_type = record.ActionType, action_hash = record.ActionHash,
                requester_user_id = record.RequesterUserId, target_employee_id = record.TargetEmployeeId,
                target_candidate_id = record.TargetCandidateId, target_position_id = record.TargetPositionId
            }, cancellationToken, saveChanges: false);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Mappings.ToDto(record);
        }
        finally
        {
            if (gateAcquired) InMemoryGate.Release();
        }
    }

    public async Task<ApprovalDto?> GetAsync(ApprovalId id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var approval = await db.Approvals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        return approval is null ? null : Mappings.ToDto(approval);
    }

    public async Task<Result<ApprovalDto>> DecideAsync(ApprovalId id, ApprovalDecision decision,
        CancellationToken cancellationToken, string? idempotencyKey = null)
    {
        var tenantId = tenant.TenantId.Value;
        var actorId = tenant.UserId.Value;
        if (actorId == Guid.Empty)
            return Result.Failure<ApprovalDto>(Error.Forbidden("Authenticated actor identity is required."));

        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        if (approval is null) return Result.Failure<ApprovalDto>(Error.NotFound());
        if (approval.RequesterUserId == Guid.Empty)
            return Result.Failure<ApprovalDto>(Error.Forbidden("Approval requester identity is required."));
        if (approval.RequesterUserId == actorId)
            return Result.Failure<ApprovalDto>(Error.Forbidden("Approval requester cannot decide their own approval."));
        await authority.EnsureCanDecideAsync(tenantId, actorId, approval.ActionType, cancellationToken);

        var normalized = decision.Decision.Trim().ToLowerInvariant();
        if (normalized is not ("approve" or "reject"))
            return Result.Failure<ApprovalDto>(Error.Validation("Decision must be approve or reject."));

        var requestHash = ComputeDecisionHash(id.Value, decision);
        var operation = $"approval-decision:{id.Value}:{actorId}";
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            try
            {
                var replay = await idempotency.GetCompletedAsync<ApprovalDto>(tenantId, idempotencyKey,
                    operation, cancellationToken, requestHash);
                if (replay is not null) return Result.Success(replay);
            }
            catch (IdempotencyConflictException)
            {
                return Result.Failure<ApprovalDto>(new Error("IDEMPOTENCY_CONFLICT",
                    "The idempotency key was already used with a different decision."));
            }
        }

        var relational = db.Database.IsRelational();
        await using var transaction = relational && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var gateAcquired = false;
        try
        {
            if (!relational)
            {
                await InMemoryGate.WaitAsync(cancellationToken);
                gateAcquired = true;
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var replay = await idempotency.GetCompletedAsync<ApprovalDto>(tenantId, idempotencyKey,
                        operation, cancellationToken, requestHash);
                    if (replay is not null) return Result.Success(replay);
                }
            }

            var now = clock.GetUtcNow();
            await authority.EnsureCanDecideAsync(tenantId, actorId, approval.ActionType, cancellationToken);
            if (relational)
            {
                var updated = await UpdateDecisionStateAsync(tenantId, id.Value, normalized, actorId, now, cancellationToken);
                if (updated == 0)
                {
                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        try
                        {
                            var replay = await idempotency.GetCompletedAsync<ApprovalDto>(tenantId, idempotencyKey,
                                operation, cancellationToken, requestHash);
                            if (replay is not null) return Result.Success(replay);
                        }
                        catch (IdempotencyConflictException)
                        {
                            return Result.Failure<ApprovalDto>(new Error("IDEMPOTENCY_CONFLICT",
                                "The idempotency key was already used with a different decision."));
                        }
                    }
                    return await DecisionFailureAsync(tenantId, id.Value, cancellationToken);
                }
                approval = await db.Approvals.AsNoTracking().SingleAsync(
                    x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
            }
            else
            {
                approval = await db.Approvals.SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
                if (approval is null) return Result.Failure<ApprovalDto>(Error.NotFound());
                if (approval.State != ApprovalState.Pending)
                    return Result.Failure<ApprovalDto>(Error.Conflict("Approval is not pending."));
                if (approval.ExpiresAt <= now)
                {
                    approval.State = ApprovalState.Expired;
                    await db.SaveChangesAsync(cancellationToken);
                    return Result.Failure<ApprovalDto>(Error.Conflict("Approval has expired."));
                }
                approval.State = normalized == "approve" ? ApprovalState.Approved : ApprovalState.Rejected;
                approval.DecidedBy = actorId;
                approval.DecidedAt = now;
            }

            var result = Mappings.ToDto(approval);
            await audit.AppendAsync(tenantId, "approval.decided", tenant.DisplayName, new
            {
                approval_id = approval.Id, decision = normalized, reason = decision.Reason, actor_id = actorId,
                decided_by = actorId, decided_at = now
            }, cancellationToken, saveChanges: false);
            db.Notifications.Add(new NotificationRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, RecipientId = actorId,
                Level = approval.Severity.ToString(), Title = $"{approval.Title}: {approval.State}",
                CreatedAt = now
            });
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                await idempotency.SaveCompletedAsync(tenantId, idempotencyKey, operation, result,
                    cancellationToken, requestHash, saveChanges: false);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Result.Success(result);
        }
        finally
        {
            if (gateAcquired) InMemoryGate.Release();
        }
    }

    private async Task<int> UpdateDecisionStateAsync(Guid tenantId, Guid approvalId, string decision,
        Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var state = decision == "approve" ? ApprovalState.Approved : ApprovalState.Rejected;
        if (db.Database.IsNpgsql())
            return await db.Approvals
                .Where(x => x.TenantId == tenantId && x.Id == approvalId
                    && x.State == ApprovalState.Pending && x.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, state)
                    .SetProperty(x => x.DecidedBy, actorId).SetProperty(x => x.DecidedAt, now), cancellationToken);

        var rowId = await db.Database.SqlQuery<long>($"""
            SELECT rowid AS "Value" FROM "Approvals"
            WHERE "TenantId" = {tenantId} AND "Id" = {approvalId}
            """).FirstOrDefaultAsync(cancellationToken);
        if (rowId == 0) return 0;
        var nowText = now.ToString("O", CultureInfo.InvariantCulture);
        var actorText = actorId.ToString("D");
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Approvals" SET "State" = {state.ToString()}, "DecidedBy" = {actorText}, "DecidedAt" = {nowText}
            WHERE rowid = {rowId} AND "State" = 'Pending'
              AND julianday("ExpiresAt") > julianday({nowText})
            """, cancellationToken);
    }

    private async Task<Result<ApprovalDto>> DecisionFailureAsync(Guid tenantId, Guid approvalId,
        CancellationToken cancellationToken)
    {
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == approvalId, cancellationToken);
        if (approval is null) return Result.Failure<ApprovalDto>(Error.NotFound());
        if (approval.State != ApprovalState.Pending)
            return Result.Failure<ApprovalDto>(Error.Conflict("Approval is not pending."));
        return Result.Failure<ApprovalDto>(Error.Conflict("Approval has expired."));
    }

    private async Task ValidateBindingAsync(ApprovalBinding binding, CancellationToken cancellationToken)
    {
        var tenantId = binding.TenantId;
        if (binding.TargetEmployeeId == Guid.Empty || binding.TargetCandidateId == Guid.Empty
            || binding.TargetPositionId == Guid.Empty)
            throw new InvalidOperationException("Approval target IDs cannot be empty.");

        switch (binding.NormalizedActionType)
        {
            case ApprovalActionTypes.EmployeeOffboard:
            case ApprovalActionTypes.EmployeeTerminate:
                await EnsureEmployeeOwnedAsync(binding.TargetEmployeeId, tenantId, cancellationToken);
                break;
            case ApprovalActionTypes.HiringHire:
                if (binding.TargetCandidateId is null || binding.TargetPositionId is null)
                    throw new InvalidOperationException("Hiring approvals require candidate and position targets.");
                var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.Id == binding.TargetCandidateId, cancellationToken)
                    ?? throw new InvalidOperationException("Approval target candidate does not belong to this tenant.");
                if (candidate.PositionId != binding.TargetPositionId)
                    throw new InvalidOperationException("Approval candidate and position targets are inconsistent.");
                await EnsurePositionOwnedAsync(binding.TargetPositionId, tenantId, cancellationToken);
                break;
            case ApprovalActionTypes.HiringRehire:
                await EnsureEmployeeOwnedAsync(binding.TargetEmployeeId, tenantId, cancellationToken);
                await EnsurePositionOwnedAsync(binding.TargetPositionId, tenantId, cancellationToken);
                break;
        }

        var employeeValidated = binding.NormalizedActionType is ApprovalActionTypes.EmployeeOffboard
            or ApprovalActionTypes.EmployeeTerminate or ApprovalActionTypes.HiringRehire;
        var candidateValidated = binding.NormalizedActionType == ApprovalActionTypes.HiringHire;
        var positionValidated = binding.NormalizedActionType is ApprovalActionTypes.HiringHire
            or ApprovalActionTypes.HiringRehire;
        if (!employeeValidated && binding.TargetEmployeeId is not null)
            await EnsureEmployeeOwnedAsync(binding.TargetEmployeeId, tenantId, cancellationToken);
        if (!candidateValidated && binding.TargetCandidateId is not null)
            await EnsureCandidateOwnedAsync(binding.TargetCandidateId, tenantId, cancellationToken);
        if (!positionValidated && binding.TargetPositionId is not null)
            await EnsurePositionOwnedAsync(binding.TargetPositionId, tenantId, cancellationToken);
    }

    private async Task EnsureEmployeeOwnedAsync(Guid? employeeId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (employeeId is null || !await db.Employees.AnyAsync(
                x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken))
            throw new InvalidOperationException("Approval target employee does not belong to this tenant.");
    }

    private async Task EnsureCandidateOwnedAsync(Guid? candidateId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (candidateId is null || !await db.Candidates.AnyAsync(
                x => x.TenantId == tenantId && x.Id == candidateId, cancellationToken))
            throw new InvalidOperationException("Approval target candidate does not belong to this tenant.");
    }

    private async Task EnsurePositionOwnedAsync(Guid? positionId, Guid tenantId, CancellationToken cancellationToken)
    {
        if (positionId is null || !await db.Positions.AnyAsync(
                x => x.TenantId == tenantId && x.Id == positionId, cancellationToken))
            throw new InvalidOperationException("Approval target position does not belong to this tenant.");
    }

    internal static string ComputeActionHash(string actionType, Guid? targetEmployeeId) =>
        new ApprovalBinding(Guid.Empty, actionType, targetEmployeeId).ComputeLegacyV1Hash();

    internal static string ComputeActionHashV2(Guid tenantId, string actionType, Guid? targetEmployeeId,
        Guid? targetCandidateId, Guid? targetPositionId) =>
        new ApprovalBinding(tenantId, actionType, targetEmployeeId, targetCandidateId, targetPositionId).ComputeV2Hash();

    private static string ComputeDecisionHash(Guid approvalId, ApprovalDecision decision)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            approval_id = approvalId,
            decision = decision.Decision.Trim().ToLowerInvariant(),
            reason = decision.Reason?.Trim() ?? string.Empty
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
