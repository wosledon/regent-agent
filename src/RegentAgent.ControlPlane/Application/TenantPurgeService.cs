using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Memory;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantPurgeService(RegentDbContext db, IObjectStore objects,
    IEnumerable<ITenantJsonDocumentStore> documentStores, IMemoryVectorStore vectors,
    JsonDocumentStore<DataExportDto> exports, IDocumentStore<DataLifecycleState>? lifecycleStore = null,
    TimeProvider? clock = null, TenantObjectUploadTracker? uploads = null)
{
    public async Task<int> PurgeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        using var purge = await TenantPurgeFence.BeginPurgeAsync(tenantId, cancellationToken);
        using var purgeWrites = TenantPurgeFence.AllowPurgeWrites(tenantId);
        try
        {
            if (uploads is not null) await uploads.WaitForDrainAsync(tenantId, cancellationToken);
            await PersistPurgingStateAsync(tenantId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A store or database can commit and still report cancellation; only release admission when no tombstone exists.
            if (!await TenantPurgeStateInspector.IsPurgeActiveAsync(db, lifecycleStore, tenantId))
                purge.AbortBeforeDurableState();
            throw;
        }

        var tenantPrefixes = TenantObjectNamespace.AllPrefixes(tenantId);
        var tenantExports = (await exports.GetAllAsync(cancellationToken))
            .Where(item => item.TenantId == tenantId).ToArray();
        foreach (var export in tenantExports)
            RequireTenantObjectKey(export.ObjectKey, tenantId);
        var artifacts = await db.Artifacts.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        foreach (var artifact in artifacts)
            RequireTenantObjectKey(artifact.ObjectKey, tenantId);

        var removed = await PurgeRelationalAsync(tenantId, cancellationToken);
        foreach (var prefix in tenantPrefixes)
        {
            removed += await objects.DeletePrefixAsync(prefix, cancellationToken);
            if (!await objects.IsPrefixEmptyAsync(prefix, cancellationToken))
                throw new InvalidOperationException("Tenant object namespace is not empty after purge.");
        }
        removed += await vectors.DeleteTenantAsync(tenantId.ToString("D"), cancellationToken);
        removed += await exports.DeleteTenantAsync(tenantId, cancellationToken);
        foreach (var store in documentStores.Where(store => store is not JsonDocumentStore<DataLifecycleState>
                     && !ReferenceEquals(store, exports)))
            removed += await store.DeleteTenantAsync(tenantId, cancellationToken);
        return removed;
    }

    private async Task PersistPurgingStateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (lifecycleStore is not null)
        {
            var now = (clock ?? TimeProvider.System).GetUtcNow();
            await lifecycleStore.MutateAsync(tenantId.ToString(), state =>
            {
                var current = state ?? new DataLifecycleState { TenantId = tenantId };
                if (current.TenantId == Guid.Empty) current.TenantId = tenantId;
                if (current.IsPurged) return current;
                var request = current.DeletionRequests.FirstOrDefault(item =>
                    item.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase)
                    && Guid.TryParse(item.ResourceId, out var resourceTenantId) && resourceTenantId == tenantId);
                if (request is null)
                {
                    request = new DeletionRequestRecord
                    {
                        TenantId = tenantId, ResourceType = "Tenant", ResourceId = tenantId.ToString(),
                        Reason = "Tenant purge requested", DeletedAt = now, RestoreUntil = now
                    };
                    current.DeletionRequests.Add(request);
                }
                request.Status = "Purging";
                request.PurgeStartedAt ??= now;
                return current;
            }, cancellationToken);
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId, cancellationToken);
        if (tenant is not null && !tenant.Status.Equals("Purging", StringComparison.OrdinalIgnoreCase))
        {
            tenant.Status = "Purging";
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<int> PurgeRelationalAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        db.Artifacts.RemoveRange(await db.Artifacts.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.WorkEntries.RemoveRange(await db.WorkEntries.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        // Audit records and checkpoints remain append-only; the runtime role has no update/delete grant.
        db.Notifications.RemoveRange(await db.Notifications.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Approvals.RemoveRange(await db.Approvals.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.InterviewStages.RemoveRange(await db.InterviewStages.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.CandidateAppeals.RemoveRange(await db.CandidateAppeals.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.FairnessAudits.RemoveRange(await db.FairnessAudits.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Candidates.RemoveRange(await db.Candidates.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.TaskCoordinations.RemoveRange(await db.TaskCoordinations.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Tasks.RemoveRange(await db.Tasks.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Milestones.RemoveRange(await db.Milestones.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Projects.RemoveRange(await db.Projects.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.EmploymentPeriods.RemoveRange(await db.EmploymentPeriods.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.OrgDelegations.RemoveRange(await db.OrgDelegations.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Positions.RemoveRange(await db.Positions.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Charters.RemoveRange(await db.Charters.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Employees.RemoveRange(await db.Employees.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Memberships.RemoveRange(await db.Memberships.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.IdempotencyRecords.RemoveRange(await db.IdempotencyRecords.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Outbox.RemoveRange(await db.Outbox.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Inbox.RemoveRange(await db.Inbox.Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken));
        db.Tenants.RemoveRange(await db.Tenants.Where(x => x.Id == tenantId).ToListAsync(cancellationToken));
        await db.SaveChangesAsync(cancellationToken);
        return db.ChangeTracker.Entries().Count();
    }

    private static void RequireTenantObjectKey(string objectKey, Guid tenantId)
    {
        if (!TenantObjectNamespace.Contains(objectKey, tenantId))
            throw new InvalidOperationException("Tenant object key is outside the tenant namespace.");
    }
}
