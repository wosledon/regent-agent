using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

internal sealed record EmployeeTerminationResult(Guid EmploymentPeriodId, IReadOnlyList<Guid> InterruptedTaskIds);

internal static class EmployeeTerminationMutation
{
    private static readonly string[] InterruptibleStatuses =
        ["Pending", "Assigned", "InProgress", "InReview", "Blocked"];

    public static async Task<EmployeeTerminationResult> ApplyAsync(RegentDbContext db, TimeProvider clock,
        Guid tenantId, Guid employeeId, string reason, CancellationToken cancellationToken)
    {
        var relational = db.Database.IsRelational();
        var employee = relational
            ? await db.Employees.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken)
            : await db.Employees.SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken);
        if (employee is null) throw new KeyNotFoundException("Employee not found.");
        if (employee.State == EmployeeState.Terminated)
            throw new InvalidOperationException("Employee is already terminated.");

        var activePeriods = await db.EmploymentPeriods
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.EndedAt == null)
            .ToListAsync(cancellationToken);
        if (activePeriods.Count != 1)
            throw new InvalidOperationException("Exactly one active employment period is required for termination.");
        var period = activePeriods[0];

        var controllerPositionId = await ControllerSuccessionPolicy.FindControllerPositionIdAsync(
            db, tenantId, employeeId, cancellationToken);
        if (controllerPositionId is { } controllerPositionIdValue)
            await ControllerSuccessionPolicy.RequireAcceptedSuccessorAsync(db, tenantId,
                controllerPositionIdValue, employeeId, cancellationToken);

        var now = clock.GetUtcNow();
        if (relational)
        {
            var transitioned = await db.Employees
                .Where(x => x.TenantId == tenantId && x.Id == employeeId
                    && x.State != EmployeeState.Terminated)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.State, EmployeeState.Terminated)
                    .SetProperty(x => x.StateReason, reason), cancellationToken);
            if (transitioned != 1)
                throw new InvalidOperationException("Employee state changed before termination completed.");
            var ended = await db.EmploymentPeriods
                .Where(x => x.TenantId == tenantId && x.Id == period.Id && x.EndedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EndedAt, now), cancellationToken);
            if (ended != 1)
                throw new InvalidOperationException("Employment period changed before termination completed.");
        }
        else
        {
            employee.State = EmployeeState.Terminated;
            employee.StateReason = reason;
            period.EndedAt = now;
        }

        var displayNameCount = await db.Employees.CountAsync(
            x => x.TenantId == tenantId && x.DisplayName == employee.DisplayName, cancellationToken);
        var activeTasksQuery = db.Tasks.Where(x => x.TenantId == tenantId
            && InterruptibleStatuses.Contains(x.Status));
        activeTasksQuery = displayNameCount == 1
            ? activeTasksQuery.Where(x => x.AssigneeEmployeeId == employeeId
                || (x.AssigneeEmployeeId == null && x.Assignee == employee.DisplayName))
            : activeTasksQuery.Where(x => x.AssigneeEmployeeId == employeeId);
        var activeTasks = await activeTasksQuery.ToListAsync(cancellationToken);
        foreach (var task in activeTasks)
        {
            task.Status = "Interrupted";
            task.LeaseOwner = null;
            task.LeaseToken = null;
            task.LeaseExpiresAt = null;
            task.UpdatedAt = now;
            task.Version++;
        }

        foreach (var positionId in activePeriods.Select(x => x.PositionId).Distinct())
        {
            if (await db.EmploymentPeriods.AnyAsync(x => x.TenantId == tenantId && x.PositionId == positionId
                    && x.EmployeeId != employeeId && x.EndedAt == null, cancellationToken))
                continue;
            var position = await db.Positions.SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == positionId, cancellationToken);
            if (position is null || position.Status == "Open") continue;
            position.Status = "Open";
            position.Version++;
        }

        return new EmployeeTerminationResult(period.Id, activeTasks.Select(x => x.Id).ToArray());
    }
}
