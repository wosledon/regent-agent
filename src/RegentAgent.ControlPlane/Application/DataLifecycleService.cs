using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class DataLifecycleService(IDocumentStore<DataLifecycleState> store,
    IObjectStore objects, RegentDbContext db, TimeProvider clock, TenantPurgeService tenantPurge, TenantRlsScope rlsScope)
{
    public async Task<DataLifecycleState> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(tenantId.ToString(), cancellationToken) ?? new DataLifecycleState { TenantId = tenantId };
        if (state.TenantId == Guid.Empty) state.TenantId = tenantId;
        else if (state.TenantId != tenantId) throw new UnauthorizedAccessException("Lifecycle state belongs to another tenant.");
        return state;
    }

    public Task<DeletionRequestRecord> SoftDeleteAsync(Guid tenantId, string resourceType, string resourceId,
        string reason, CancellationToken cancellationToken, DateTimeOffset? restoreUntil = null) => MutateAsync(tenantId, state =>
    {
        if (resourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(resourceId, out var requestedTenant) || requestedTenant != tenantId)
                throw new KeyNotFoundException("Tenant not found.");
            var tenant = db.Tenants.SingleOrDefault(x => x.Id == tenantId);
            if (tenant is null) throw new KeyNotFoundException("Tenant not found.");
            tenant.Status = "SoftDeleted";
        }
        var days = resourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase) ? 7 : 30;
        var request = new DeletionRequestRecord
        {
            TenantId = tenantId, ResourceType = resourceType, ResourceId = resourceId,
            Reason = reason, DeletedAt = clock.GetUtcNow(), RestoreUntil = restoreUntil ?? clock.GetUtcNow().AddDays(days)
        };
        state.DeletionRequests.Add(request);
        return (true, request);
    }, cancellationToken);

    public async Task<DeletionProof> PurgeTenantNowAsync(Guid tenantId, string reason,
        CancellationToken cancellationToken)
    {
        var request = await SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), reason,
            cancellationToken, clock.GetUtcNow());
        return await PurgeAsync(tenantId, request.Id, "PURGE", cancellationToken);
    }

    public Task<DeletionRequestRecord> RestoreAsync(Guid tenantId, Guid requestId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var request = Find(state.DeletionRequests, requestId);
            if (request.Status != "SoftDeleted" || clock.GetUtcNow() > request.RestoreUntil)
                throw new InvalidOperationException("Deletion request is not restorable.");
            request.Status = "Restored";
            if (request.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase))
            {
                var tenant = db.Tenants.SingleOrDefault(x => x.Id == tenantId);
                if (tenant is not null) tenant.Status = "Active";
            }
            return (true, request);
        }, cancellationToken);

    public Task<LegalHoldRecord> CreateLegalHoldAsync(Guid tenantId, LegalHoldRecord hold,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(hold.ResourceRef) || string.IsNullOrWhiteSpace(hold.AuthorizedBy))
            throw new InvalidOperationException("Legal hold scope and authorization are required.");
        hold.TenantId = tenantId;
        hold.StartsAt = clock.GetUtcNow();
        state.LegalHolds.Add(hold);
        return (true, hold);
    }, cancellationToken);

    public Task<LegalHoldRecord> ReleaseLegalHoldAsync(Guid tenantId, Guid holdId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var hold = Find(state.LegalHolds, holdId);
            hold.Status = "Released";
            return (true, hold);
        }, cancellationToken);

    public Task<RetentionPolicyRecord> CreateRetentionPolicyAsync(Guid tenantId, RetentionPolicyRecord policy,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (policy.HotDays < 0 || policy.ArchiveDays < policy.HotDays || policy.ColdDays < policy.ArchiveDays
            || policy.DeleteAfterDays < policy.ColdDays) throw new InvalidOperationException("Retention periods are invalid.");
        policy.TenantId = tenantId;
        state.RetentionPolicies.Add(policy);
        return (true, policy);
    }, cancellationToken);

    public Task<ArchiveManifest> ArchiveAsync(Guid tenantId, string period, string tier, int recordCount,
        CancellationToken cancellationToken) => MutateAsync(tenantId, async state =>
    {
        if (tier is not ("Archive" or "Cold")) throw new InvalidOperationException("Archive tier must be Archive or Cold.");
        var id = Guid.NewGuid();
        var manifest = new
        {
            id, tenant_id = tenantId, period, tier, record_count = recordCount, created_at = clock.GetUtcNow()
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var key = $"tenants/{tenantId}/archives/{period}/{tier.ToLowerInvariant()}/{id}.json";
        await using var stream = new MemoryStream(bytes);
        var stored = await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/json"), cancellationToken);
        var record = new ArchiveManifest
        {
            Id = id, TenantId = tenantId, Period = period, Tier = tier, ObjectKey = key,
            Sha256 = stored.Sha256, RecordCount = recordCount, CreatedAt = stored.StoredAt
        };
        state.Archives.Add(record);
        return (true, record);
    }, cancellationToken);

    public Task<ArchiveManifest> RestoreArchiveAsync(Guid tenantId, Guid archiveId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, async state =>
        {
            var archive = Find(state.Archives, archiveId);
            var metadata = await objects.GetMetadataAsync(archive.ObjectKey, cancellationToken)
                ?? throw new KeyNotFoundException("Archive object not found.");
            if (metadata.Sha256 != archive.Sha256) throw new InvalidOperationException("Archive checksum mismatch.");
            await using var stream = await objects.OpenReadAsync(archive.ObjectKey, cancellationToken)
                ?? throw new KeyNotFoundException("Archive object not found.");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Archive is empty.");
            archive.Status = "Restored";
            return (true, archive);
        }, cancellationToken);

    public Task<BackupRunRecord> BackupAsync(Guid tenantId, string profile, int rpoMinutes, int rtoMinutes,
        CancellationToken cancellationToken) => MutateAsync(tenantId, async state =>
    {
        var files = Directory.Exists("data")
            ? Directory.GetFiles("data", "*", SearchOption.AllDirectories)
                .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}objects{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(FileInfoHash).ToArray()
            : [];
        var manifest = JsonSerializer.Serialize(new { tenant_id = tenantId, created_at = clock.GetUtcNow(), files });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var id = Guid.NewGuid();
        var key = $"tenants/{tenantId}/backups/{DateTime.UtcNow:yyyyMMddHHmmss}-{id}.json";
        await using var stream = new MemoryStream(bytes);
        var stored = await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/json"), cancellationToken);
        var record = new BackupRunRecord
        {
            Id = id, TenantId = tenantId, Profile = profile, ManifestObjectKey = key,
            Sha256 = stored.Sha256, RpoMinutes = rpoMinutes, RtoMinutes = rtoMinutes, CreatedAt = stored.StoredAt
        };
        state.Backups.Add(record);
        return (true, record);
    }, cancellationToken);

    public Task<BackupRunRecord> VerifyRestoreAsync(Guid tenantId, Guid backupId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, async state =>
        {
            var backup = Find(state.Backups, backupId);
            await using var stream = await objects.OpenReadAsync(backup.ManifestObjectKey, cancellationToken)
                ?? throw new KeyNotFoundException("Backup manifest not found.");
            using var hash = SHA256.Create();
            var digest = Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
            if (digest != backup.Sha256) throw new InvalidOperationException("Backup checksum mismatch.");
            backup.RestoreVerified = true;
            backup.Status = "RestoreVerified";
            return (true, backup);
        }, cancellationToken);

    public async Task<DeletionProof> PurgeAsync(Guid tenantId, Guid requestId, string confirmation,
        CancellationToken cancellationToken)
    {
        if (confirmation != "PURGE") throw new InvalidOperationException("Explicit PURGE confirmation is required.");
        var current = await store.GetAsync(tenantId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException("Deletion request not found.");
        if (current.IsPurged)
        {
            var existing = current.DeletionProofs.FirstOrDefault(proof => proof.RequestId == requestId);
            if (existing is not null) return existing;
            throw new InvalidOperationException("Tenant lifecycle has been purged.");
        }
        var request = Find(current.DeletionRequests, requestId);
        if (request.Status == "Purged") throw new InvalidOperationException("Deletion request is already purged.");
        var isTenantPurge = request.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase);

        if (isTenantPurge)
        {
            if (request.Status != "Purging")
            {
                using (var purge = await TenantPurgeFence.BeginPurgeAsync(tenantId, cancellationToken))
                using (TenantPurgeFence.AllowPurgeWrites(tenantId))
                {
                    try
                    {
                        await store.MutateAsync(tenantId.ToString(), state =>
                        {
                            var pending = state ?? throw new InvalidOperationException("Lifecycle tombstone disappeared during purge.");
                            if (pending.TenantId == Guid.Empty) pending.TenantId = tenantId;
                            else if (pending.TenantId != tenantId)
                                throw new UnauthorizedAccessException("Lifecycle state belongs to another tenant.");
                            if (pending.IsPurged) throw new InvalidOperationException("Tenant lifecycle has been purged.");

                            var pendingRequest = Find(pending.DeletionRequests, requestId);
                            ValidateTenantObjectKeys(tenantId, pending);
                            if (pendingRequest.Status != "Purging")
                                ValidatePurgeEligibility(pending, pendingRequest);

                            pendingRequest.Status = "Purging";
                            pendingRequest.PurgeStartedAt ??= clock.GetUtcNow();
                            return pending;
                        }, cancellationToken);
                    }
                    catch
                    {
                        // Validation can fail before the tombstone commits; only keep admission closed after a durable Purging state.
                        if (!await TenantPurgeStateInspector.IsPurgeActiveAsync(db, store, tenantId))
                            purge.AbortBeforeDurableState();
                        throw;
                    }

                    var tenant = db.Tenants.SingleOrDefault(x => x.Id == tenantId);
                    if (tenant is not null) tenant.Status = "Purging";
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            await tenantPurge.PurgeAsync(tenantId, cancellationToken);
        }

        using var purgeWrites = isTenantPurge ? TenantPurgeFence.AllowPurgeWrites(tenantId) : null;
        var finalized = await store.MutateAsync(tenantId.ToString(), state =>
        {
            var pending = state ?? throw new InvalidOperationException("Lifecycle tombstone disappeared during purge.");
            if (pending.TenantId == Guid.Empty) pending.TenantId = tenantId;
            else if (pending.TenantId != tenantId)
                throw new UnauthorizedAccessException("Lifecycle state belongs to another tenant.");
            if (pending.IsPurged) throw new InvalidOperationException("Tenant lifecycle has been purged.");
            if (!isTenantPurge && pending.IsPurging)
                throw new InvalidOperationException("Tenant lifecycle is being purged.");

            var pendingRequest = Find(pending.DeletionRequests, requestId);
            if (!isTenantPurge && pendingRequest.Status != "Purging")
                ValidatePurgeEligibility(pending, pendingRequest);
            var manifest = JsonSerializer.Serialize(new { pendingRequest.Id, pendingRequest.ResourceType,
                pendingRequest.ResourceId, pendingRequest.DeletedAt });
            var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
            pendingRequest.Status = "Purged";
            pendingRequest.PurgedAt = clock.GetUtcNow();
            var proof = new DeletionProof
            {
                RequestId = pendingRequest.Id, ResourceType = pendingRequest.ResourceType, ResourceId = pendingRequest.ResourceId,
                PurgedAt = clock.GetUtcNow(), ManifestHash = manifestHash, RemainingBackupIds = []
            };
            proof.ProofHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { proof.Id, proof.RequestId, proof.ManifestHash, proof.PurgedAt })))).ToLowerInvariant();
            if (isTenantPurge)
            {
                pending.DeletionRequests.Clear();
                pending.LegalHolds.Clear();
                pending.RetentionPolicies.Clear();
                pending.Archives.Clear();
                pending.Backups.Clear();
                pending.Rights.Clear();
                pending.Licenses.Clear();
            }
            pending.DeletionProofs.Add(proof);
            return pending;
        }, cancellationToken);
        return finalized.DeletionProofs.Single(proof => proof.RequestId == requestId);
    }

    public Task<RightsRecord> RecordRightsAsync(Guid tenantId, RightsRecord rights,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        rights.TenantId = tenantId;
        rights.PublishStatus = rights.ThirdPartyRights == "Unknown" || rights.SourceRefs.Count == 0
            ? "Blocked" : "Eligible";
        state.Rights.Add(rights);
        return (true, rights);
    }, cancellationToken);

    public Task<LicenseRecord> RecordLicenseAsync(Guid tenantId, LicenseRecord license,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        license.TenantId = tenantId;
        license.PublishStatus = string.IsNullOrWhiteSpace(license.License) || string.IsNullOrWhiteSpace(license.NoticeRef)
            || string.IsNullOrWhiteSpace(license.SbomRef) || !license.ContributorConfirmed || !license.TrademarkReviewed
            ? "Blocked" : "Eligible";
        state.Licenses.Add(license);
        return (true, license);
    }, cancellationToken);

    public async Task<int> RunDuePurgeAsync(CancellationToken cancellationToken)
    {
        var states = await store.GetAllAsync(cancellationToken);
        var purged = 0;
        foreach (var state in states)
        {
            var due = state.DeletionRequests.Where(x => x.Status == "Purging"
                || (x.Status == "SoftDeleted" && x.RestoreUntil <= clock.GetUtcNow())).ToArray();
            foreach (var request in due)
            {
                var blocked = state.LegalHolds.Where(IsActiveHold).Any(hold => BlocksPurge(hold, request));
                if (blocked) continue;
                await rlsScope.ExecuteAsync(state.TenantId, null,
                    token => PurgeAsync(state.TenantId, request.Id, "PURGE", token), cancellationToken);
                purged++;
                if (request.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase)) break;
            }
        }
        return purged;
    }

    public async Task<LifecycleEvaluation> EvaluateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await GetAsync(tenantId, cancellationToken);
        var due = state.DeletionRequests.Where(x => x.Status == "SoftDeleted" && x.RestoreUntil <= clock.GetUtcNow()).ToArray();
        var holds = state.LegalHolds.Where(IsActiveHold).ToArray();
        var blocked = due.Where(request => holds.Any(hold => BlocksPurge(hold, request))).Select(x => x.Id).ToArray();
        return new LifecycleEvaluation
        {
            ArchiveCandidates = state.RetentionPolicies.Select(x => x.DataClass).ToArray(),
            PurgeCandidates = due.Select(x => x.Id).ToArray(), LegalHoldBlocked = blocked
        };
    }

    private Task<T> MutateAsync<T>(Guid tenantId, Func<DataLifecycleState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken) => MutateCoreAsync(tenantId, state => Task.FromResult(mutation(state)),
        cancellationToken, allowPurged: false);

    private Task<T> MutateAsync<T>(Guid tenantId, Func<DataLifecycleState, Task<(bool Changed, T Result)>> mutation,
        CancellationToken cancellationToken, bool allowPurged = false) => MutateCoreAsync(tenantId, mutation,
        cancellationToken, allowPurged);

    private async Task<T> MutateCoreAsync<T>(Guid tenantId,
        Func<DataLifecycleState, Task<(bool Changed, T Result)>> mutation, CancellationToken cancellationToken,
        bool allowPurged)
    {
        var result = default(T);
        await store.MutateAsync(tenantId.ToString(), async state =>
        {
            var current = state ?? new DataLifecycleState { TenantId = tenantId };
            if (current.TenantId == Guid.Empty) current.TenantId = tenantId;
            else if (current.TenantId != tenantId) throw new UnauthorizedAccessException("Lifecycle state belongs to another tenant.");
            if ((current.IsPurged || current.IsPurging) && !allowPurged)
                throw new InvalidOperationException("Tenant lifecycle is being purged or has been purged.");
            var (changed, mutationResult) = await mutation(current);
            if (changed) await db.SaveChangesAsync(cancellationToken);
            result = mutationResult;
            return current;
        }, cancellationToken);
        return result!;
    }

    private void ValidatePurgeEligibility(DataLifecycleState state, DeletionRequestRecord request)
    {
        if (request.Status == "Purged") throw new InvalidOperationException("Deletion request is already purged.");
        if (request.Status == "Restored") throw new InvalidOperationException("Restored deletion request cannot be purged.");
        if (request.Status != "SoftDeleted") throw new InvalidOperationException("Deletion request is not purgeable.");
        if (clock.GetUtcNow() < request.RestoreUntil) throw new InvalidOperationException("Restore window has not expired.");
        if (state.LegalHolds.Any(hold => IsActiveHold(hold) && BlocksPurge(hold, request)))
            throw new InvalidOperationException("Legal hold blocks physical purge.");
    }

    private static bool BlocksPurge(LegalHoldRecord hold, DeletionRequestRecord request) =>
        hold.ResourceRef == request.ResourceId
        || hold.ResourceRef == $"tenant:{request.ResourceId}"
        || hold.ResourceRef == "*";

    private static void ValidateTenantObjectKeys(Guid tenantId, DataLifecycleState state)
    {
        foreach (var key in state.Archives.Select(x => x.ObjectKey)
                     .Concat(state.Backups.Select(x => x.ManifestObjectKey))
                     .Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            if (!TenantObjectNamespace.Contains(key, tenantId))
                throw new InvalidOperationException("Lifecycle object key is outside the tenant namespace.");
        }
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");

    private bool IsActiveHold(LegalHoldRecord hold)
    {
        var now = clock.GetUtcNow();
        return hold.Status == "Active" && hold.StartsAt <= now && (hold.ExpiresAt is null || hold.ExpiresAt > now);
    }

    private static object FileInfoHash(string path)
    {
        using var stream = File.OpenRead(path);
        return new { path, length = stream.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
    }
}
