using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Primitives;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class EmployeeService(RegentDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<IReadOnlyList<EmployeeSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.EmployeeNo).ToListAsync(cancellationToken);
        return employees.Select(x => Mappings.ToSummary(x, Freshness.Fresh)).ToArray();
    }

    public async Task<EmployeeTimeline?> GetTimelineAsync(EmployeeId id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var employee = await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        if (employee is null)
        {
            return null;
        }

        var entries = (await db.WorkEntries.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == id.Value)
            .ToListAsync(cancellationToken)).OrderByDescending(x => x.OccurredAt).Take(100).ToList();
        return new EmployeeTimeline(Mappings.ToSummary(employee, Freshness.Fresh),
            entries.Select(Mappings.ToWorkEntry).ToArray(), clock.GetUtcNow());
    }
}
