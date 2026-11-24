using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.AgentIdentity;
using RegentAgent.Contracts.Runtime;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TaskExecutionService(RegentDbContext db, IObjectStore objects, TimeProvider clock,
    AuditIntegrityService audit, AgentAssignmentTokenService assignmentTokens)
{
    public async Task<TaskClaim?> ClaimAsync(string runnerId, Guid tenantId, CancellationToken cancellationToken)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var now = clock.GetUtcNow();
        var candidates = await db.Tasks.Where(x => x.TenantId == tenantId &&
            (x.Status == "Pending" || x.Status == "Assigned")).ToListAsync(cancellationToken);
        var task = candidates.FirstOrDefault(x => x.Status == "Pending" || x.LeaseExpiresAt < now);
        if (task is null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var employeeName = string.IsNullOrWhiteSpace(task.Assignee) ? "Local Agent" : task.Assignee;
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.DisplayName == employeeName, cancellationToken);
        if (employee is null)
        {
            employee = new EmployeeRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
                DisplayName = employeeName, Position = "Runner", State = RegentAgent.Contracts.Enums.EmployeeState.Active
            };
            db.Employees.Add(employee);
        }
        task.Assignee = employee.DisplayName;
        task.Status = "Assigned";
        task.LeaseOwner = runnerId;
        task.LeaseToken = Guid.NewGuid().ToString("N");
        task.LeaseExpiresAt = now.AddMinutes(2);
        task.Attempt += 1;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var scopes = new[] { "tasks:execute", "chat:write", "infer:employee" };
        var assignment = assignmentTokens.Issue(new AgentAssignmentRequest(tenantId, employee.Id, task.Id, runnerId, scopes));
        return new TaskClaim(task.Id, task.Title, task.TaskType, task.ProjectId, task.LeaseToken,
            task.LeaseExpiresAt.Value, tenantId, employee.Id, assignment.Token, assignment.Identity.ExpiresAt, scopes, runnerId);
    }

    public async Task<bool> CheckpointAsync(Guid taskId, TaskCheckpoint checkpoint, Guid tenantId,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null || task.LeaseToken != checkpoint.LeaseToken) return false;
        task.CheckpointJson = checkpoint.CheckpointJson;
        task.LeaseExpiresAt = clock.GetUtcNow().AddMinutes(2);
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
        if (task is null || task.LeaseToken != completion.LeaseToken || task.LeaseExpiresAt < clock.GetUtcNow())
        {
            return false;
        }

        task.Status = completion.Status == "Failed" ? "Failed" : "Done";
        task.LeaseOwner = null;
        task.LeaseToken = null;
        task.LeaseExpiresAt = null;
        task.CheckpointJson = null;
        var project = await db.Projects.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == task.ProjectId, cancellationToken);
        var employeeName = string.IsNullOrWhiteSpace(task.Assignee) ? "Local Runner" : task.Assignee;
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.DisplayName == employeeName, cancellationToken);
        if (employee is null)
        {
            employee = new EmployeeRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
                DisplayName = employeeName, Position = "Runner", State = RegentAgent.Contracts.Enums.EmployeeState.Active
            };
            db.Employees.Add(employee);
        }

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
        return true;
    }
}
