using Microsoft.EntityFrameworkCore;
using RegentAgent.Agents;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class ControlPlaneTenantRuntimeAdmission(RegentDbContext db) : ITenantRuntimeAdmission
{
    public async Task EnsureActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var status = await db.Tenants.AsNoTracking().Where(x => x.Id == tenantId).Select(x => x.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (status is null) throw new KeyNotFoundException("Tenant not found.");
        if (!status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            throw new TenantPurgeInProgressException(tenantId.ToString());
    }
}
