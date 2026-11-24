using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.AgentIdentity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Runtime;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TaskExecutionService(RegentDbContext db, IObjectStore objects, TimeProvider clock,
    AuditIntegrityService audit, AgentAssignmentTokenService assignmentTokens,
    DeliveryRuntimeProjectionService deliveryProjection, ITenantQuotaGuard quotaGuard)
{
    public async Task<TaskClaim?> ClaimAsync(string runnerId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var now = clock.GetUtcNow();
        var candidates = await db.Tasks.Where(x => x.TenantId == tenantId &&
            (x.Status == "Pending" || x.Status == "Assigned" || x.Status == "InProgress")
            && !db.Employees.Any(employee => employee.TenantId == tenantId
                && employee.State == RegentAgent.Contracts.Enums.EmployeeState.Terminated
                && (employee.Id == x.AssigneeEmployeeId
                    || (x.AssigneeEmployeeId == null && employee.DisplayName == x.Assignee))))
            .ToListAsync(cancellationToken);
        var task = candidates.Where(x => x.Status == "Pending").OrderBy(x => x.UpdatedAt).FirstOrDefault()
            ?? candidates.Where(x => x.Status == "Assigned" && (x.LeaseExpiresAt is null || x.LeaseExpiresAt <= now))
                .OrderBy(x => x.UpdatedAt).FirstOrDefault()
            ?? candidates.Where(x => x.Status == "InProgress" && x.LeaseExpiresAt is not null && x.LeaseExpiresAt <= now)
                .OrderBy(x => x.LeaseExpiresAt).FirstOrDefault();
        if (task is null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var employeeName = string.IsNullOrWhiteSpace(task.Assignee) ? "Local Agent" : task.Assignee;
        var employee = await ResolveEmployeeAsync(tenantId, employeeName, task.AssigneeEmployeeId, cancellationToken);
        task.Assignee = employee.DisplayName;
        task.AssigneeEmployeeId = employee.Id;
        task.AccountableEmployeeId ??= employee.Id;
        task.Status = "InProgress";
        task.LeaseOwner = runnerId;
        task.LeaseToken = Guid.NewGuid().ToString("N");
        task.LeaseExpiresAt = now.AddMinutes(2);
        task.Attempt += 1;
        task.Version++;
        task.UpdatedAt = now;
        var delegation = await db.TaskCoordinations.SingleOrDefaultAsync(x => x.TenantId == tenantId
            && x.TaskId == task.Id && x.Kind == "Delegation" && x.Status == "Accepted"
            && x.TargetEmployeeId == employee.Id, cancellationToken);
        if (delegation is not null)
        {
            delegation.Status = "InProgress";
            delegation.UpdatedAt = now;
            delegation.Version++;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await deliveryProjection.MarkRuntimeClaimedAsync(tenantId, task.Id, cancellationToken);
        var scopes = new HashSet<string>(AgentScopes.Baseline, StringComparer.OrdinalIgnoreCase);
        var period = await db.EmploymentPeriods.AsNoTracking().FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.EmployeeId == employee.Id && x.EndedAt == null, cancellationToken);
        if (period is not null)
        {
            var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == period.PositionId, cancellationToken);
            if (position is not null)
                foreach (var capability in JsonSerializer.Deserialize<string[]>(position.CapabilitiesJson) ?? [])
                    scopes.Add(capability);
        }
        var scopeArray = scopes.ToArray();
        var assignment = assignmentTokens.Issue(new AgentAssignmentRequest(tenantId, employee.Id, task.Id, runnerId, scopeArray));
        return new TaskClaim(task.Id, task.Title, task.TaskType, task.ProjectId, task.LeaseToken,
            task.LeaseExpiresAt.Value, tenantId, employee.Id, assignment.Token, assignment.Identity.ExpiresAt, scopeArray, runnerId);
    }

    public async Task<bool> CheckpointAsync(Guid taskId, TaskCheckpoint checkpoint, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null || task.LeaseToken != checkpoint.LeaseToken || task.Status == "Interrupted"
            || task.LeaseExpiresAt <= clock.GetUtcNow()) return false;
        task.CheckpointJson = checkpoint.CheckpointJson;
        task.LeaseExpiresAt = clock.GetUtcNow().AddMinutes(2);
        task.Version++;
        task.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CompleteAsync(Guid taskId, TaskCompletion completion, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null || task.LeaseToken != completion.LeaseToken || task.Status == "Interrupted"
            || task.LeaseExpiresAt <= clock.GetUtcNow())
        {
            return false;
        }

        task.Status = completion.Status == "Failed" ? "Failed" : "Done";
        task.LeaseOwner = null;
        task.LeaseToken = null;
        task.LeaseExpiresAt = null;
        task.CheckpointJson = null;
        task.Version++;
        task.UpdatedAt = clock.GetUtcNow();
        var delegation = await db.TaskCoordinations.SingleOrDefaultAsync(x => x.TenantId == tenantId
            && x.TaskId == task.Id && x.Kind == "Delegation" && x.Status == "InProgress"
            && x.TargetEmployeeId == task.AssigneeEmployeeId, cancellationToken);
        if (delegation is not null)
        {
            delegation.Status = "Completed";
            delegation.CompletedAt = delegation.UpdatedAt = clock.GetUtcNow();
            delegation.Version++;
        }
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == task.ProjectId, cancellationToken);
        var employeeName = string.IsNullOrWhiteSpace(task.Assignee) ? "Local Runner" : task.Assignee;
        var employee = await ResolveEmployeeAsync(tenantId, employeeName, task.AssigneeEmployeeId, cancellationToken);

        var workId = Guid.NewGuid();
        db.WorkEntries.Add(new WorkEntryRecord
        {
            Id = workId, TenantId = tenantId, EmployeeId = employee.Id, TaskId = task.Id,
            OccurredAt = clock.GetUtcNow(), Title = task.Title, EventType = "task.completed",
            Status = task.Status, NextAction = task.Status == "Done" ? null : "Review failure",
            CostMinor = task.CostMinor, Currency = task.Currency,
            EvidenceJson = JsonSerializer.Serialize(completion.EvidenceRefs)
        });

        var artifactId = Guid.NewGuid();
        var objectKey = $"tenants/{tenantId}/projects/{task.ProjectId}/artifacts/{artifactId}";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(completion.Output));
        var stored = await objects.PutAsync(new ObjectUploadRequest(objectKey, stream, "text/markdown"), cancellationToken);
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = artifactId, TenantId = tenantId, ProjectId = task.ProjectId, TaskId = task.Id,
            ObjectKey = objectKey, Hash = stored.Sha256, ContentType = stored.ContentType,
            Length = stored.Length, UploadState = "Ready", CreatedAt = clock.GetUtcNow()
        });

        await audit.AppendAsync(tenantId, "task.completed", employeeName,
            new { task_id = task.Id, status = task.Status, artifact_id = artifactId, project = project.Name }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await deliveryProjection.MarkRuntimeCompletedAsync(tenantId, task.Id, cancellationToken);
        return true;
    }

    private async Task<EmployeeRecord> ResolveEmployeeAsync(Guid tenantId, string employeeName, Guid? employeeId,
        CancellationToken cancellationToken)
    {
        var employee = employeeId is { } id
            ? await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken)
            : null;
        if (employeeId is not null && employee is null) throw new KeyNotFoundException("Assigned employee not found.");
        employee ??= await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.DisplayName == employeeName, cancellationToken);
        if (employee is not null) return employee;
        await quotaGuard.EnsureAsync(tenantId, "employees", 1, cancellationToken);
        employee = new EmployeeRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
            DisplayName = employeeName, Position = "Runner", State = RegentAgent.Contracts.Enums.EmployeeState.Active
        };
        db.Employees.Add(employee);
        return employee;
    }
}
