using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Projects;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Projects;

namespace RegentAgent.ControlPlane.Application;

public sealed class DeliveryRuntimeProjectionService(RegentDbContext db, DeliveryService delivery,
    AuditIntegrityService audit, TimeProvider clock)
{
    public async Task SyncApprovedPlanAsync(Guid tenantId, Guid projectId, DecompositionPlan plan,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == projectId, cancellationToken)
            ?? throw new KeyNotFoundException("Project not found.");
        var created = 0;
        foreach (var task in plan.Tasks)
        {
            if (await db.Tasks.AnyAsync(x => x.TenantId == tenantId && x.Id == task.Id, cancellationToken)) continue;
            db.Tasks.Add(new TaskRecord
            {
                Id = task.Id, TenantId = tenantId, ProjectId = projectId, Title = task.Title,
                TaskType = task.TaskType, Status = "Pending", Assignee = task.Assignee ?? string.Empty,
                Weight = task.Weight, Currency = project.Currency, UpdatedAt = clock.GetUtcNow()
            });
            created++;
        }
        if (created == 0) return;
        await db.SaveChangesAsync(cancellationToken);
        await audit.AppendAsync(tenantId, "delivery.plan.runtime_projected", "Controller", new
        {
            project_id = projectId, plan_id = plan.Id, task_count = created
        }, cancellationToken);
    }

    public async Task MarkRuntimeClaimedAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null) return;
        var state = await delivery.GetAsync(tenantId, task.ProjectId, cancellationToken);
        if (state.Tasks.All(item => item.Id != taskId)) return;
        await delivery.MarkRuntimeClaimedAsync(tenantId, task.ProjectId, taskId, task.Assignee, cancellationToken);
    }

    public async Task MarkRuntimeCompletedAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null) return;
        var state = await delivery.GetAsync(tenantId, task.ProjectId, cancellationToken);
        if (state.Tasks.All(item => item.Id != taskId)) return;
        await delivery.MarkRuntimeCompletedAsync(tenantId, task.ProjectId, taskId, task.Assignee, cancellationToken);
    }

    public async Task SyncRuntimeStatusAsync(Guid tenantId, Guid taskId, string status, string assignee,
        CancellationToken cancellationToken)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null) return;
        task.Status = status;
        if (!string.IsNullOrWhiteSpace(assignee)) task.Assignee = assignee;
        task.UpdatedAt = clock.GetUtcNow();
        task.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }
}
