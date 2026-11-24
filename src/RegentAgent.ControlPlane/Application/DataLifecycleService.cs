using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class DataLifecycleService(JsonDocumentStore<DataLifecycleState> store,
    IObjectStore objects, RegentDbContext db, TimeProvider clock, TenantPurgeService tenantPurge)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<DataLifecycleState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    public Task<DeletionRequestRecord> SoftDeleteAsync(Guid tenantId, string resourceType, string resourceId,
        string reason, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
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
            Reason = reason, DeletedAt = clock.GetUtcNow(), RestoreUntil = clock.GetUtcNow().AddDays(days)
        };
        state.DeletionRequests.Add(request);
        return (true, request);
    }, cancellationToken);

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
    }, cancellationToken, true);

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
        }, cancellationToken, true);

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
    }, cancellationToken, true);

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
        }, cancellationToken, true);

    public Task<DeletionProof> PurgeAsync(Guid tenantId, Guid requestId, string confirmation,
        CancellationToken cancellationToken) => MutateAsync(tenantId, async state =>
    {
        if (confirmation != "PURGE") throw new InvalidOperationException("Explicit PURGE confirmation is required.");
        var request = Find(state.DeletionRequests, requestId);
        if (request.Status == "Purged") throw new InvalidOperationException("Deletion request is already purged.");
        if (clock.GetUtcNow() < request.RestoreUntil) throw new InvalidOperationException("Restore window has not expired.");
        var holds = state.LegalHolds.Where(IsActiveHold).Where(x => x.ResourceRef == request.ResourceId
            || x.ResourceRef == $"tenant:{request.ResourceId}" || x.ResourceRef == "*").ToArray();
        if (holds.Length > 0) throw new InvalidOperationException("Legal hold blocks physical purge.");
        var manifest = JsonSerializer.Serialize(new { request.Id, request.ResourceType, request.ResourceId, request.DeletedAt });
        var manifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        if (request.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase))
            await tenantPurge.PurgeAsync(tenantId, cancellationToken);
        request.Status = "Purged";
        var proof = new DeletionProof
        {
            RequestId = request.Id, ResourceType = request.ResourceType, ResourceId = request.ResourceId,
            PurgedAt = clock.GetUtcNow(), ManifestHash = manifestHash,
            RemainingBackupIds = state.Backups.Where(x => x.TenantId == tenantId).Select(x => x.Id).ToArray()
        };
        proof.ProofHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { proof.Id, proof.RequestId, proof.ManifestHash, proof.PurgedAt })))).ToLowerInvariant();
        state.DeletionProofs.Add(proof);
        return (true, proof);
    }, cancellationToken, true);

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
            var due = state.DeletionRequests.Where(x => x.Status == "SoftDeleted" && x.RestoreUntil <= clock.GetUtcNow()).ToArray();
            foreach (var request in due)
            {
                var blocked = state.LegalHolds.Where(IsActiveHold).Any(x => x.ResourceRef == request.ResourceId
                    || x.ResourceRef == $"tenant:{request.ResourceId}" || x.ResourceRef == "*");
                if (blocked) continue;
                await PurgeAsync(state.TenantId, request.Id, "PURGE", cancellationToken);
                purged++;
            }
        }
        return purged;
    }

    public Task<LifecycleEvaluation> EvaluateAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var due = state.DeletionRequests.Where(x => x.Status == "SoftDeleted" && x.RestoreUntil <= clock.GetUtcNow()).ToArray();
            var holds = state.LegalHolds.Where(IsActiveHold).ToArray();
            var blocked = due.Where(x => holds.Any(h => h.ResourceRef == x.ResourceId || h.ResourceRef == "*")).Select(x => x.Id).ToArray();
            return (false, new LifecycleEvaluation
            {
                ArchiveCandidates = state.RetentionPolicies.Select(x => x.DataClass).ToArray(),
                PurgeCandidates = due.Select(x => x.Id).ToArray(), LegalHoldBlocked = blocked
            });
        }, cancellationToken);

    private Task<T> MutateAsync<T>(Guid tenantId, Func<DataLifecycleState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken) => MutateCoreAsync(tenantId, state => Task.FromResult(mutation(state)), cancellationToken);

    private Task<T> MutateAsync<T>(Guid tenantId, Func<DataLifecycleState, Task<(bool Changed, T Result)>> mutation,
        CancellationToken cancellationToken, bool asyncMutation) => MutateCoreAsync(tenantId, mutation, cancellationToken);

    private async Task<T> MutateCoreAsync<T>(Guid tenantId,
        Func<DataLifecycleState, Task<(bool Changed, T Result)>> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.GetAsync(tenantId.ToString(), cancellationToken) ?? new DataLifecycleState();
            if (state.TenantId == Guid.Empty) state.TenantId = tenantId;
            else if (state.TenantId != tenantId) throw new UnauthorizedAccessException("Lifecycle state belongs to another tenant.");
            var (changed, result) = await mutation(state);
            if (changed)
            {
                await store.PutAsync(tenantId.ToString(), state, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
        finally { gate.Release(); }
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");

    private static bool IsActiveHold(LegalHoldRecord hold) =>
        hold.Status == "Active" && hold.StartsAt <= DateTimeOffset.UtcNow && (hold.ExpiresAt is null || hold.ExpiresAt > DateTimeOffset.UtcNow);

    private static object FileInfoHash(string path)
    {
        using var stream = File.OpenRead(path);
        return new { path, length = stream.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
    }
}
