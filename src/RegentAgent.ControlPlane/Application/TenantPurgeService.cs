using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantPurgeService(RegentDbContext db, IObjectStore objects,
    IEnumerable<ITenantJsonDocumentStore> documentStores)
{
    public async Task<int> PurgeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var artifacts = await db.Artifacts.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        foreach (var artifact in artifacts) await objects.DeleteAsync(artifact.ObjectKey, cancellationToken);
        var removed = await PurgeRelationalAsync(tenantId, cancellationToken);
        foreach (var store in documentStores) removed += await store.DeleteTenantAsync(tenantId, cancellationToken);
        return removed;
    }

    private async Task<int> PurgeRelationalAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        db.Artifacts.RemoveRange(await db.Artifacts.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.WorkEntries.RemoveRange(await db.WorkEntries.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.AuditCheckpoints.RemoveRange(await db.AuditCheckpoints.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Notifications.RemoveRange(await db.Notifications.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Approvals.RemoveRange(await db.Approvals.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.InterviewStages.RemoveRange(await db.InterviewStages.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.CandidateAppeals.RemoveRange(await db.CandidateAppeals.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.FairnessAudits.RemoveRange(await db.FairnessAudits.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Candidates.RemoveRange(await db.Candidates.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.TaskCoordinations.RemoveRange(await db.TaskCoordinations.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Tasks.RemoveRange(await db.Tasks.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        var projectIds = await db.Projects.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToListAsync(cancellationToken);
        db.Milestones.RemoveRange(await db.Milestones.Where(x => projectIds.Contains(x.ProjectId)).ToListAsync(cancellationToken));
        db.Projects.RemoveRange(await db.Projects.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.EmploymentPeriods.RemoveRange(await db.EmploymentPeriods.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.OrgDelegations.RemoveRange(await db.OrgDelegations.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Positions.RemoveRange(await db.Positions.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Charters.RemoveRange(await db.Charters.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Employees.RemoveRange(await db.Employees.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Memberships.RemoveRange(await db.Memberships.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Tenants.RemoveRange(await db.Tenants.Where(x => x.Id == tenantId).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return db.ChangeTracker.Entries().Count();
    }
}
