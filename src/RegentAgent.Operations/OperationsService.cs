using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed record TenantAccount(string Id, string Name, string Status, int Employees, int RunnerCount);
public sealed record SupportGrantRecord(string Id, string TenantId, string OperatorId, string Scope, DateTimeOffset ExpiresAt, string Status);
public sealed record OpsAuditRecord(string Id, string OperatorId, string Action, string Target, DateTimeOffset CreatedAt);

public sealed class OperationsService(
    JsonDocumentStore<TenantAccount> tenants,
    JsonDocumentStore<SupportGrantRecord> grants,
    JsonDocumentStore<BackupProfileRecord> backups,
    JsonDocumentStore<OpsAuditRecord> audits,
    JsonDocumentStore<BackupJobRecord> backupJobs,
    JsonDocumentStore<RestoreJobRecord> restoreJobs,
    IObjectStore objects,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await tenants.GetAllAsync(cancellationToken)).Count == 0)
            await tenants.PutAsync("demo", new TenantAccount("demo", "Regent Demo", "Active", 4, 1), cancellationToken);
        if ((await backups.GetAllAsync(cancellationToken)).Count == 0)
            await backups.PutAsync("default", DefaultProfile(), cancellationToken);
    }

    public Task<IReadOnlyList<TenantAccount>> TenantsAsync(CancellationToken cancellationToken) => tenants.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<SupportGrantRecord>> GrantsAsync(CancellationToken cancellationToken) => grants.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<BackupProfileRecord>> BackupsAsync(CancellationToken cancellationToken) => backups.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<BackupJobRecord>> BackupJobsAsync(CancellationToken cancellationToken) => backupJobs.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<RestoreJobRecord>> RestoreJobsAsync(CancellationToken cancellationToken) => restoreJobs.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<OpsAuditRecord>> AuditsAsync(CancellationToken cancellationToken) => audits.GetAllAsync(cancellationToken);

    public async Task<SupportGrantRecord> CreateGrantAsync(string tenantId, string operatorId, string scope, int minutes, CancellationToken cancellationToken)
    {
        var grant = new SupportGrantRecord(Guid.NewGuid().ToString("N"), tenantId, operatorId, scope,
            DateTimeOffset.UtcNow.AddMinutes(minutes), "Pending");
        await grants.PutAsync(grant.Id, grant, cancellationToken);
        await AuditAsync(operatorId, "support_grant.created", tenantId, cancellationToken);
        return grant;
    }

    public async Task<SupportGrantRecord> ApproveGrantAsync(string id, string operatorId, CancellationToken cancellationToken)
    {
        var grant = await grants.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Grant not found.");
        if (grant.Status != "Pending") throw new InvalidOperationException("Only pending grants can be approved.");
        grant = grant with { Status = "Active" };
        await grants.PutAsync(id, grant, cancellationToken);
        await AuditAsync(operatorId, "support_grant.approved", id, cancellationToken);
        return grant;
    }

    public async Task<TenantAccount> SetTenantStatusAsync(string tenantId, string status, string operatorId,
        CancellationToken cancellationToken)
    {
        if (status is not ("Active" or "Suspended")) throw new InvalidOperationException("Invalid tenant status.");
        var tenant = await tenants.GetAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException("Tenant not found.");
        tenant = tenant with { Status = status };
        await tenants.PutAsync(tenantId, tenant, cancellationToken);
        await AuditAsync(operatorId, "tenant.status_changed", tenantId, cancellationToken);
        return tenant;
    }

    public async Task RevokeGrantAsync(string id, string operatorId, CancellationToken cancellationToken)
    {
        var grant = await grants.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Grant not found.");
        await grants.PutAsync(id, grant with { Status = "Revoked" }, cancellationToken);
        await AuditAsync(operatorId, "support_grant.revoked", id, cancellationToken);
    }

    public Task SetBackupAsync(bool enabled, string components, string operatorId, CancellationToken cancellationToken) =>
        ConfigureBackupAsync(DefaultProfile() with
        {
            Enabled = enabled, Components = components, RetentionDays = enabled ? 35 : 0,
            PostgresFull = enabled && components.Contains("postgres", StringComparison.OrdinalIgnoreCase),
            RustFs = enabled && components.Contains("rustfs", StringComparison.OrdinalIgnoreCase),
            KeyVault = enabled && components.Contains("keyvault", StringComparison.OrdinalIgnoreCase)
        }, operatorId, cancellationToken);

    public async Task<BackupProfileRecord> ConfigureBackupAsync(BackupProfileRecord profile, string operatorId,
        CancellationToken cancellationToken)
    {
        ValidateProfile(profile);
        var existing = await backups.GetAsync(profile.Id, cancellationToken);
        profile = profile with { Version = (existing?.Version ?? 0) + 1 };
        await backups.PutAsync(profile.Id, profile, cancellationToken);
        await AuditAsync(operatorId, "backup.profile_updated", profile.Id, cancellationToken);
        return profile;
    }

    public async Task<IReadOnlyList<BackupJobRecord>> RunBackupAsync(string profileId, string operatorId,
        CancellationToken cancellationToken)
    {
        var profile = await backups.GetAsync(profileId, cancellationToken) ?? throw new KeyNotFoundException("Backup profile not found.");
        if (!profile.Enabled) throw new InvalidOperationException("Backup profile is disabled.");
        var components = EnabledComponents(profile);
        if (components.Count == 0) throw new InvalidOperationException("No backup components are enabled.");
        var jobs = new List<BackupJobRecord>();
        foreach (var component in components)
        {
            var job = new BackupJobRecord
            {
                ProfileId = profileId, ProfileVersion = profile.Version, Component = component,
                Status = "Running", StartedAt = clock.GetUtcNow(),
                RetentionUntil = clock.GetUtcNow().AddDays(profile.RetentionDays)
            };
            try
            {
                var files = ComponentFiles(component);
                if (component == "postgres" && files.Count == 0)
                    throw new InvalidOperationException("No local database snapshot is available; configure a Postgres backup connector.");
                var manifest = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    backup_id = job.Id, profile_id = profileId, profile_version = profile.Version,
                    component, mode = "local-evidence", created_at = job.StartedAt, files
                }, JsonOptions);
                var key = $"backups/{profileId}/{job.Id:N}/{component}.json";
                await using var stream = new MemoryStream(manifest);
                var stored = await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/json"), cancellationToken);
                job.ManifestRef = stored.ObjectKey;
                job.SnapshotRef = key;
                job.Sha256 = stored.Sha256;
                job.Status = "Completed";
            }
            catch (Exception exception)
            {
                job.Status = "Blocked";
                job.Error = exception.Message;
            }
            job.CompletedAt = clock.GetUtcNow();
            await backupJobs.PutAsync(job.Id.ToString(), job, cancellationToken);
            jobs.Add(job);
        }
        await AuditAsync(operatorId, "backup.run", profileId, cancellationToken);
        return jobs;
    }

    public async Task<BackupCoverageDto> GetCoverageAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = await backups.GetAsync(profileId, cancellationToken) ?? DefaultProfile();
        if (!profile.Enabled)
            return new BackupCoverageDto(profileId, false, "not_configured", [], null, null, null, "not_configured", "not_configured");
        var components = EnabledComponents(profile);
        var jobs = await backupJobs.GetAllAsync(cancellationToken);
        var latest = components.Select(component => jobs.Where(job => job.ProfileId == profileId
            && job.Component == component && job.Status == "Completed").OrderByDescending(job => job.CompletedAt).FirstOrDefault()).ToArray();
        DateTimeOffset? lastSuccessful = latest.All(job => job is not null) ? latest.Min(job => job!.CompletedAt) : null;
        var restores = await restoreJobs.GetAllAsync(cancellationToken);
        var lastRestore = restores.Where(job => job.ProfileId == profileId && job.Status == "Verified")
            .OrderByDescending(job => job.VerifiedAt).FirstOrDefault();
        return new BackupCoverageDto(profileId, true, lastSuccessful is null ? "unverified" : "covered", components,
            lastSuccessful, lastSuccessful is null ? null : Math.Max(0, (int)Math.Ceiling((clock.GetUtcNow() - lastSuccessful.Value).TotalMinutes)),
            lastRestore?.RtoActualMinutes, lastSuccessful is null ? "unverified" : "measured",
            lastRestore is null ? "unverified" : "measured");
    }

    public async Task<RestoreJobRecord> RequestRestoreAsync(RestoreRequest request, CancellationToken cancellationToken)
    {
        if (request.BackupIds.Count == 0 || string.IsNullOrWhiteSpace(request.ApprovalRef)
            || string.IsNullOrWhiteSpace(request.Requester) || request.Scope is not ("Full" or "Tenant"))
            throw new InvalidOperationException("Restore requires backups, scope, requester and approval reference.");
        var jobs = await backupJobs.GetAllAsync(cancellationToken);
        if (request.BackupIds.Any(id => !jobs.Any(job => job.Id == id && job.Status == "Completed")))
            throw new InvalidOperationException("A selected backup is missing or incomplete.");
        var restore = new RestoreJobRecord
        {
            ProfileId = jobs.First(job => request.BackupIds.Contains(job.Id)).ProfileId,
            BackupIds = request.BackupIds.Distinct().ToArray(), Scope = request.Scope,
            ApprovalRef = request.ApprovalRef, Requester = request.Requester, RequestedAt = clock.GetUtcNow()
        };
        await restoreJobs.PutAsync(restore.Id.ToString(), restore, cancellationToken);
        return restore;
    }

    public async Task<RestoreJobRecord> ApproveRestoreAsync(Guid id, RestoreApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var restore = await restoreJobs.GetAsync(id.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Restore job not found.");
        if (restore.Status != "PendingApproval") throw new InvalidOperationException("Restore is not pending approval.");
        if (string.IsNullOrWhiteSpace(request.Approver) || string.IsNullOrWhiteSpace(request.Resolution)
            || request.Approver.Equals(restore.Requester, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A different authorized approver and resolution are required.");
        var jobs = (await backupJobs.GetAllAsync(cancellationToken)).Where(job => restore.BackupIds.Contains(job.Id)).ToArray();
        foreach (var job in jobs)
        {
            await using var stream = await objects.OpenReadAsync(job.ManifestRef, cancellationToken)
                ?? throw new InvalidOperationException($"Backup manifest missing for {job.Component}.");
            using var hash = SHA256.Create();
            var digest = Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
            if (digest != job.Sha256) throw new InvalidOperationException($"Backup checksum mismatch for {job.Component}.");
        }
        var now = clock.GetUtcNow();
        restore.Status = "Verified";
        restore.Approver = request.Approver;
        restore.VerifiedAt = now;
        restore.RpoActualMinutes = Math.Max(0, (int)Math.Ceiling((now - jobs.Min(job => job.CompletedAt!.Value)).TotalMinutes));
        restore.RtoActualMinutes = Math.Max(0, (int)Math.Ceiling((now - restore.RequestedAt).TotalMinutes));
        var report = JsonSerializer.SerializeToUtf8Bytes(new
        {
            restore_id = restore.Id, approval_ref = restore.ApprovalRef, verified_at = now,
            mode = "isolated-local-manifest-verification", backup_ids = restore.BackupIds,
            rpo_actual_minutes = restore.RpoActualMinutes, rto_actual_minutes = restore.RtoActualMinutes,
            resolution = request.Resolution
        }, JsonOptions);
        var key = $"restores/{restore.Id:N}/report.json";
        await using var reportStream = new MemoryStream(report);
        restore.ReportRef = (await objects.PutAsync(new ObjectUploadRequest(key, reportStream, "application/json"), cancellationToken)).ObjectKey;
        await restoreJobs.PutAsync(restore.Id.ToString(), restore, cancellationToken);
        await AuditAsync(request.Approver, "backup.restore_verified", restore.Id.ToString(), cancellationToken);
        return restore;
    }

    private static BackupProfileRecord DefaultProfile() => new("default", "local", false, "none", 0);

    private static List<string> EnabledComponents(BackupProfileRecord profile)
    {
        var components = new List<string>();
        if (profile.PostgresFull) components.Add("postgres");
        if (profile.PostgresPitr) components.Add("postgres_pitr");
        if (profile.RustFs) components.Add("rustfs");
        if (profile.KeyVault) components.Add("keyvault");
        return components;
    }

    private static void ValidateProfile(BackupProfileRecord profile)
    {
        if (profile.RetentionDays < 0 || profile.RetentionDays > 3650
            || (profile.Enabled && profile.RetentionDays == 0))
            throw new InvalidOperationException("Enabled profiles require retention between 1 and 3650 days.");
        if (!profile.Enabled && (profile.PostgresFull || profile.PostgresPitr || profile.RustFs || profile.KeyVault || profile.Offsite || profile.RestoreDrills))
            throw new InvalidOperationException("Component switches require master backup enabled.");
        if (profile.PostgresPitr && !profile.PostgresFull) throw new InvalidOperationException("PITR requires Postgres full backup.");
        if (profile.Offsite && !(profile.PostgresFull || profile.RustFs || profile.KeyVault)) throw new InvalidOperationException("Offsite requires a local backup component.");
        if (profile.RestoreDrills && EnabledComponents(profile).Count == 0) throw new InvalidOperationException("Restore drills require a recoverable component.");
        if (profile.Offsite && string.IsNullOrWhiteSpace(profile.TargetRef)) throw new InvalidOperationException("Offsite requires an explicit target reference.");
    }

    private static List<object> ComponentFiles(string component)
    {
        var paths = component switch
        {
            "postgres" => Directory.Exists("data") ? Directory.GetFiles("data", "*.db", SearchOption.AllDirectories) : [],
            "postgres_pitr" => Directory.Exists("data") ? Directory.GetFiles("data", "*wal*", SearchOption.AllDirectories) : [],
            "rustfs" => Directory.Exists("data/objects") ? Directory.GetFiles("data/objects", "*", SearchOption.AllDirectories) : [],
            "keyvault" => File.Exists("data/keyvault.json") ? ["data/keyvault.json"] : [],
            _ => []
        };
        return paths.Where(path => !path.Contains("master.key", StringComparison.OrdinalIgnoreCase)).Select(path =>
        {
            using var stream = File.OpenRead(path);
            var info = new FileInfo(path);
            return (object)new { path, length = info.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
        }).ToList();
    }

    private async Task AuditAsync(string operatorId, string action, string target, CancellationToken cancellationToken)
    {
        var record = new OpsAuditRecord(Guid.NewGuid().ToString("N"), operatorId, action, target, DateTimeOffset.UtcNow);
        await audits.PutAsync(record.Id, record, cancellationToken);
    }
}
