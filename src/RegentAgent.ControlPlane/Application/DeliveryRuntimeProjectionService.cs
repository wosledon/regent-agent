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
        var uniqueEmployeeIds = await LoadUniqueEmployeeIdsAsync(tenantId, cancellationToken);
        var created = 0;
        foreach (var task in plan.Tasks)
        {
            if (await db.Tasks.AnyAsync(x => x.TenantId == tenantId && x.Id == task.Id, cancellationToken)) continue;
            if (task.AssigneeEmployeeId is { } stableAssigneeId
                && !await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == stableAssigneeId, cancellationToken))
                throw new UnauthorizedAccessException("Task assignee does not belong to this tenant.");
            var assigneeEmployeeId = task.AssigneeEmployeeId ?? (task.Assignee is { } assignee
                && uniqueEmployeeIds.TryGetValue(assignee, out var employeeId)
                ? employeeId
                : (Guid?)null);
            db.Tasks.Add(new TaskRecord
            {
                Id = task.Id, TenantId = tenantId, ProjectId = projectId, Title = task.Title,
                TaskType = task.TaskType, Status = "Pending", Assignee = task.Assignee ?? string.Empty,
                AssigneeEmployeeId = assigneeEmployeeId,
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
        await delivery.MarkRuntimeClaimedAsync(tenantId, task.ProjectId, taskId, task.Assignee,
            task.AssigneeEmployeeId, cancellationToken);
    }

    public async Task MarkRuntimeCompletedAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null) return;
        var state = await delivery.GetAsync(tenantId, task.ProjectId, cancellationToken);
        if (state.Tasks.All(item => item.Id != taskId)) return;
        await delivery.MarkRuntimeCompletedAsync(tenantId, task.ProjectId, taskId, task.Assignee,
            task.AssigneeEmployeeId, cancellationToken);
    }

    public async Task RequireTaskAssigneeAsync(Guid tenantId, Guid projectId, Guid taskId,
        Guid employeeId, bool requireAssigned, CancellationToken cancellationToken)
    {
        var task = await db.Tasks.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == taskId, cancellationToken)
            ?? throw new KeyNotFoundException("Runtime task not found.");
        if (task.ProjectId != projectId) throw new UnauthorizedAccessException("Task belongs to another project.");
        if (requireAssigned)
        {
            if (task.AssigneeEmployeeId != employeeId)
                throw new UnauthorizedAccessException("Task is assigned to a different employee.");
            return;
        }
        if (task.AssigneeEmployeeId is { } existingEmployeeId && existingEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Task is assigned to a different employee.");
    }

    public async Task SyncRuntimeStatusAsync(Guid tenantId, Guid taskId, string status, string assignee,
        CancellationToken cancellationToken, Guid? assigneeEmployeeId = null)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken);
        if (task is null) return;
        task.Status = status;
        if (!string.IsNullOrWhiteSpace(assignee)) task.Assignee = assignee;
        if (assigneeEmployeeId is { } stableAssigneeId)
        {
            if (task.AssigneeEmployeeId is { } existingAssigneeId && existingAssigneeId != stableAssigneeId)
                throw new UnauthorizedAccessException("Task is assigned to a different employee.");
            task.AssigneeEmployeeId = stableAssigneeId;
        }
        else if (task.AssigneeEmployeeId is null)
            task.AssigneeEmployeeId = await FindUniqueEmployeeIdAsync(tenantId, task.Assignee, cancellationToken);
        task.UpdatedAt = clock.GetUtcNow();
        task.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid?> FindUniqueEmployeeIdAsync(Guid tenantId, string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return null;
        var employees = await db.Employees.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.DisplayName }).ToListAsync(cancellationToken);
        var matches = employees.Where(x => string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1 ? matches[0].Id : null;
    }

    private async Task<Dictionary<string, Guid>> LoadUniqueEmployeeIdsAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.DisplayName }).ToListAsync(cancellationToken);
        return employees.GroupBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);
    }
}
