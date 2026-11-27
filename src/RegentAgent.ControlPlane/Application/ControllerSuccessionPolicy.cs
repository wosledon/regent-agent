using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

internal static class ControllerSuccessionPolicy
{
    public const string ControllerPositionName = "Controller";

    public static async Task<Guid?> FindControllerPositionIdAsync(RegentDbContext db, Guid tenantId,
        Guid employeeId, CancellationToken cancellationToken)
    {
        var activePositionIds = await db.EmploymentPeriods.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.EndedAt == null)
            .Select(x => x.PositionId).ToListAsync(cancellationToken);
        if (activePositionIds.Count == 0) return null;
        if (activePositionIds.Count > 1)
            throw new InvalidOperationException("Exactly one active employment period is required.");

        var positionId = activePositionIds[0];
        return await db.Positions.AsNoTracking().AnyAsync(
            x => x.TenantId == tenantId && x.Id == positionId && x.Name == ControllerPositionName,
            cancellationToken)
            ? positionId
            : null;
    }

    public static Task RequireAcceptedSuccessorAsync(RegentDbContext db, Guid tenantId,
        Guid controllerPositionId, Guid controllerEmployeeId, CancellationToken cancellationToken) =>
        RequireAcceptedSuccessorAsync(db, tenantId, controllerPositionId, [controllerEmployeeId], cancellationToken);

    public static async Task RequireAcceptedSuccessorForPositionAsync(RegentDbContext db, Guid tenantId,
        Guid controllerPositionId, CancellationToken cancellationToken)
    {
        var activePeriods = await db.EmploymentPeriods.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PositionId == controllerPositionId
                && x.Kind == "Primary" && x.EndedAt == null)
            .OrderBy(x => x.StartedAt).Select(x => x.EmployeeId).ToListAsync(cancellationToken);
        var activeEmployeeIds = activePeriods.Distinct().ToList();
        if (activeEmployeeIds.Count < 2)
            throw new InvalidOperationException("Controller changes require an active accepted successor.");
        await RequireAcceptedSuccessorAsync(db, tenantId, controllerPositionId,
            activeEmployeeIds.Take(1), cancellationToken);
    }

    private static async Task RequireAcceptedSuccessorAsync(RegentDbContext db, Guid tenantId,
        Guid controllerPositionId, IEnumerable<Guid> controllerEmployeeIds,
        CancellationToken cancellationToken)
    {
        var excluded = controllerEmployeeIds.ToHashSet();
        var hasAcceptedSuccessor = await db.EmploymentPeriods.AnyAsync(
            x => x.TenantId == tenantId && x.PositionId == controllerPositionId
                && x.Kind == "Primary" && x.EndedAt == null
                && !excluded.Contains(x.EmployeeId)
                && db.Employees.Any(employee => employee.TenantId == tenantId
                    && employee.Id == x.EmployeeId && employee.State == EmployeeState.Active),
            cancellationToken);
        if (!hasAcceptedSuccessor)
            throw new InvalidOperationException("Controller changes require an active accepted successor.");
    }
}
