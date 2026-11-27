using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed record ApprovalExecutionResult(
    Guid ApprovalId,
    string ActionType,
    Guid? EmployeeId,
    Guid? CandidateId,
    Guid? PositionId,
    string Status,
    DateTimeOffset ExecutedAt,
    string? Reason = null);

public sealed class ApprovalExecutionService(
    RegentDbContext db,
    ITenantContext tenant,
    TimeProvider clock,
    AuditIntegrityService audit,
    IdempotencyService idempotency,
    ITenantQuotaGuard quotaGuard,
    ApprovalAuthority authority)
{
    private const string ExecutionEventType = ApprovalExecutionEvent.EventType;

    public Task<ApprovalExecutionResult> OffboardAsync(Guid employeeId, string approvalId,
        string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new ApprovalBinding(tenant.TenantId.Value, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: employeeId), approvalId, idempotencyKey,
            async token => await TerminateMutationAsync(employeeId, ApprovalActionTypes.EmployeeOffboard,
                "Offboarded after Boss approval", token), cancellationToken);

    public Task<ApprovalExecutionResult> TerminateAsync(Guid employeeId, string approvalId,
        string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new ApprovalBinding(tenant.TenantId.Value, ApprovalActionTypes.EmployeeTerminate,
                TargetEmployeeId: employeeId), approvalId, idempotencyKey,
            async token => await TerminateMutationAsync(employeeId, ApprovalActionTypes.EmployeeTerminate,
                "Terminated after Boss approval", token), cancellationToken);

    public async Task<ApprovalExecutionResult> HireAsync(Guid candidateId, string approvalId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenant.TenantId.Value && x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Candidate not found.");
        return await ExecuteAsync(new ApprovalBinding(tenant.TenantId.Value, ApprovalActionTypes.HiringHire,
                TargetCandidateId: candidateId, TargetPositionId: candidate.PositionId), approvalId, idempotencyKey,
            token => HireMutationAsync(candidateId, candidate.PositionId, token), cancellationToken);
    }

    public Task<ApprovalExecutionResult> RehireAsync(Guid employeeId, Guid positionId, string approvalId,
        string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new ApprovalBinding(tenant.TenantId.Value, ApprovalActionTypes.HiringRehire,
                TargetEmployeeId: employeeId, TargetPositionId: positionId), approvalId, idempotencyKey,
            token => RehireMutationAsync(employeeId, positionId, token), cancellationToken);

    private async Task<ApprovalExecutionResult> ExecuteAsync(ApprovalBinding binding, string approvalId,
        string idempotencyKey, Func<CancellationToken, Task<ExecutionMutation>> mutate,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var actorId = tenant.UserId.Value;
        if (actorId == Guid.Empty) throw new InvalidOperationException("Authenticated actor identity is required.");
        if (!Guid.TryParse(approvalId, out var parsedApprovalId) || parsedApprovalId == Guid.Empty)
            throw new InvalidOperationException("A valid approval ID is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidOperationException("Idempotency key is required.");
        await authority.EnsureCanExecuteAsync(tenantId, actorId, binding.NormalizedActionType, cancellationToken);

        var operation = $"approval-execution:{parsedApprovalId}:{actorId}";
        var requestHash = binding.ComputeV2Hash();
        try
        {
            var replay = await idempotency.GetCompletedAsync<ApprovalExecutionResult>(tenantId, idempotencyKey,
                operation, cancellationToken, requestHash);
            if (replay is not null) return replay;
        }
        catch (IdempotencyConflictException)
        {
            throw;
        }

        var relational = db.Database.IsRelational();
        await using var mutationScope = await TenantMutationScope.BeginAsync(db, tenantId, cancellationToken);
        try
        {
            if (!relational)
            {
                var replay = await idempotency.GetCompletedAsync<ApprovalExecutionResult>(tenantId, idempotencyKey,
                    operation, cancellationToken, requestHash);
                if (replay is not null) return replay;
            }

            var now = clock.GetUtcNow();
            await authority.EnsureCanExecuteAsync(tenantId, actorId, binding.NormalizedActionType, cancellationToken);
            if (relational)
            {
                var consumed = await ConsumeApprovalAsync(tenantId, parsedApprovalId, binding, requestHash,
                    actorId, now, cancellationToken);
                if (consumed == 0)
                {
                    var replay = await idempotency.GetCompletedAsync<ApprovalExecutionResult>(tenantId,
                        idempotencyKey, operation, cancellationToken, requestHash);
                    if (replay is not null) return replay;
                    await ThrowApprovalFailureAsync(tenantId, parsedApprovalId, cancellationToken);
                }
            }
            else
            {
                var approval = await db.Approvals.SingleOrDefaultAsync(
                    x => x.TenantId == tenantId && x.Id == parsedApprovalId, cancellationToken)
                    ?? throw new KeyNotFoundException("Approval not found.");
                EnsureExecutable(approval, binding, now);
            }

            var mutation = await mutate(cancellationToken);
            if (!relational)
            {
                var approval = await db.Approvals.SingleAsync(
                    x => x.TenantId == tenantId && x.Id == parsedApprovalId, cancellationToken);
                approval.ConsumedAt = now;
                approval.ConsumedBy = actorId;
            }

            var result = new ApprovalExecutionResult(parsedApprovalId, binding.NormalizedActionType,
                mutation.EmployeeId, mutation.CandidateId, mutation.PositionId, "Completed", now,
                binding.NormalizedActionType switch
                {
                    ApprovalActionTypes.EmployeeOffboard => "Offboarded after Boss approval",
                    ApprovalActionTypes.EmployeeTerminate => "Terminated after Boss approval",
                    _ => null
                });
            var auditPayload = new Dictionary<string, object?>
            {
                ["approval_id"] = parsedApprovalId, ["actor_id"] = actorId,
                ["action_type"] = binding.NormalizedActionType,
                ["target_employee_id"] = binding.TargetEmployeeId,
                ["target_candidate_id"] = binding.TargetCandidateId,
                ["target_position_id"] = binding.TargetPositionId
            };
            foreach (var property in JsonSerializer.SerializeToElement(mutation.Payload).EnumerateObject())
                auditPayload[property.Name] = property.Value.Clone();
            await audit.AppendAsync(tenantId, mutation.AuditEvent, tenant.DisplayName, auditPayload,
                cancellationToken, saveChanges: false);
            await StageOutboxAsync(tenantId, binding, actorId, parsedApprovalId, mutation, now, cancellationToken);
            await idempotency.SaveCompletedAsync(tenantId, idempotencyKey, operation, result,
                cancellationToken, requestHash, saveChanges: false);
            await db.SaveChangesAsync(cancellationToken);
            await mutationScope.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (!relational) db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<ExecutionMutation> TerminateMutationAsync(Guid employeeId, string actionType,
        string reason, CancellationToken cancellationToken)
    {
        var interrupted = await EmployeeTerminationMutation.ApplyAsync(db, clock, tenant.TenantId.Value,
            employeeId, reason, cancellationToken);
        return new ExecutionMutation(employeeId, null, null,
            actionType == ApprovalActionTypes.EmployeeOffboard ? "employee.offboarded" : "employee.terminated",
            new { employee_id = employeeId, interrupted_task_ids = interrupted.InterruptedTaskIds },
            interrupted.EmploymentPeriodId);
    }

    private async Task<ExecutionMutation> HireMutationAsync(Guid candidateId, Guid positionId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var candidate = await db.Candidates.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Candidate not found.");
        if (candidate.Status is not ("Assessed" or "Offered"))
            throw new InvalidOperationException("Candidate must be assessed or offered before hiring.");
        var position = await db.Positions.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == positionId, cancellationToken)
            ?? throw new KeyNotFoundException("Candidate position not found.");
        if (candidate.PositionId != position.Id || position.Status != "Open")
            throw new InvalidOperationException("Candidate position is not open for hiring.");

        await EnsureHiringCapacityAsync(tenantId, cancellationToken);
        var number = await db.Employees.CountAsync(x => x.TenantId == tenantId, cancellationToken) + 1;
        var employee = new EmployeeRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{number:D4}",
            DisplayName = candidate.DisplayName, Position = position.Name, State = EmployeeState.Active
        };
        db.Employees.Add(employee);
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            PositionId = position.Id, Kind = "Primary", StartedAt = clock.GetUtcNow()
        });
        candidate.Status = "Hired";
        await FillPositionAsync(position, cancellationToken);
        return new ExecutionMutation(employee.Id, candidate.Id, position.Id, "candidate.hired",
            new { candidate_id = candidate.Id, employee_id = employee.Id, position_id = position.Id });
    }

    private async Task<ExecutionMutation> RehireMutationAsync(Guid employeeId, Guid positionId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var employee = await db.Employees.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee not found.");
        if (employee.State != EmployeeState.Terminated)
            throw new InvalidOperationException("Only terminated employees can be rehired.");
        var position = await db.Positions.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == positionId, cancellationToken)
            ?? throw new KeyNotFoundException("Position not found.");
        if (position.Status != "Open") throw new InvalidOperationException("Rehire position must be open.");

        await EnsureHiringCapacityAsync(tenantId, cancellationToken);
        employee.State = EmployeeState.Active;
        employee.StateReason = null;
        employee.Position = position.Name;
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            PositionId = position.Id, Kind = "Primary", StartedAt = clock.GetUtcNow()
        });
        await FillPositionAsync(position, cancellationToken);
        return new ExecutionMutation(employee.Id, null, position.Id, "employee.rehired",
            new { employee_id = employee.Id, position_id = position.Id, employee_no = employee.EmployeeNo });
    }

    private async Task EnsureHiringCapacityAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (await db.Employees.CountAsync(x => x.TenantId == tenantId && x.State != EmployeeState.Terminated,
                cancellationToken) >= 20)
            throw new InvalidOperationException("Headcount limit reached.");
        await quotaGuard.EnsureAsync(tenantId, "employees", 1, cancellationToken);
    }

    private async Task FillPositionAsync(PositionRecord position, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var tenantId = position.TenantId;
            var positionId = position.Id;
            var filled = await db.Positions
                .Where(x => x.TenantId == tenantId && x.Id == positionId && x.Status == "Open")
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, "Filled")
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (filled != 1) throw new InvalidOperationException("Position is no longer open.");
        }
        else if (position.Status != "Open")
        {
            throw new InvalidOperationException("Position is no longer open.");
        }
        position.Status = "Filled";
        position.Version++;
    }

    private async Task<int> ConsumeApprovalAsync(Guid tenantId, Guid approvalId, ApprovalBinding binding,
        string requestHash, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            var actionType = binding.NormalizedActionType;
            var targetEmployeeId = binding.TargetEmployeeId;
            var targetCandidateId = binding.TargetCandidateId;
            var targetPositionId = binding.TargetPositionId;
            return await db.Approvals
                .Where(x => x.TenantId == tenantId && x.Id == approvalId
                    && x.State == ApprovalState.Approved && x.ActionType == actionType
                    && x.TargetEmployeeId == targetEmployeeId
                    && x.TargetCandidateId == targetCandidateId
                    && x.TargetPositionId == targetPositionId
                    && x.ActionHash == requestHash && x.ConsumedAt == null && x.ExpiresAt > now
                    && x.DecidedAt != null && x.DecidedAt <= now)
                .Where(ApprovalExecutionRules.HasValidDeciderExpression)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now)
                    .SetProperty(x => x.ConsumedBy, actorId), cancellationToken);
        }

        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException("Approval not found.");
        EnsureExecutable(approval, binding, now);
        return await db.Approvals.Where(x => x.Id == approvalId && x.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ConsumedAt, now)
                .SetProperty(x => x.ConsumedBy, actorId), cancellationToken);
    }

    private static void EnsureExecutable(ApprovalRecord approval, ApprovalBinding binding, DateTimeOffset now)
    {
        if (approval.State != ApprovalState.Approved || approval.ActionType != binding.NormalizedActionType
            || approval.TargetEmployeeId != binding.TargetEmployeeId
            || approval.TargetCandidateId != binding.TargetCandidateId
            || approval.TargetPositionId != binding.TargetPositionId
            || !binding.MatchesV2Hash(approval.ActionHash) || approval.ConsumedAt is not null
            || approval.ExpiresAt <= now || approval.DecidedAt is null || approval.DecidedAt > now
            || !ApprovalExecutionRules.HasValidDecider(approval))
            throw new InvalidOperationException("An approved, unexpired and unused approval for this exact target is required.");
    }

    private async Task ThrowApprovalFailureAsync(Guid tenantId, Guid approvalId, CancellationToken cancellationToken)
    {
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == approvalId, cancellationToken)
            ?? throw new KeyNotFoundException("Approval not found.");
        if (approval.State != ApprovalState.Approved)
            throw new InvalidOperationException("Approval is not approved.");
        if (approval.ConsumedAt is not null)
            throw new InvalidOperationException("Approval has already been consumed.");
        if (approval.ExpiresAt <= clock.GetUtcNow())
            throw new InvalidOperationException("Approval has expired.");
        throw new InvalidOperationException("Approval does not match the requested action and target.");
    }

    private async Task StageOutboxAsync(Guid tenantId, ApprovalBinding binding, Guid actorId, Guid approvalId,
        ExecutionMutation mutation, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = ApprovalExecutionEvent.DeterministicId(approvalId);
        if (await db.Outbox.AnyAsync(x => x.Id == id, cancellationToken)) return;
        var reason = binding.NormalizedActionType switch
        {
            ApprovalActionTypes.EmployeeOffboard => "Offboarded after Boss approval",
            ApprovalActionTypes.EmployeeTerminate => "Terminated after Boss approval",
            _ => string.Empty
        };
        db.Outbox.Add(new OutboxRecord
        {
            Id = id, TenantId = tenantId, EventType = ExecutionEventType,
            PayloadJson = JsonSerializer.Serialize(new
            {
                approval_id = approvalId, tenant_id = tenantId, actor_id = actorId,
                action_type = binding.NormalizedActionType, target_employee_id = binding.TargetEmployeeId,
                target_candidate_id = binding.TargetCandidateId, target_position_id = binding.TargetPositionId,
                employee_id = mutation.EmployeeId, employment_period_id = mutation.EmploymentPeriodId,
                reason, occurred_at = now
            }),
            Status = "Pending", Attempts = 0, CreatedAt = now
        });
    }

    private sealed record ExecutionMutation(Guid? EmployeeId, Guid? CandidateId, Guid? PositionId,
        string AuditEvent, object Payload, Guid? EmploymentPeriodId = null);
}
