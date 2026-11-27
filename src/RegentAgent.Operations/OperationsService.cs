using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed record TenantAccount(string Id, string Name, string Status, int Employees, int RunnerCount);
public sealed record SupportGrantRecord(string Id, string TenantId, string OperatorId, string Scope, DateTimeOffset ExpiresAt, string Status)
{
    public string? ApprovalRef { get; init; }
    public string? ApprovedBy { get; init; }
}
public sealed record SupportGrantApprovalRecord(string Reference, string TenantId, string Scope,
    DateTimeOffset ExpiresAt, string ApprovedBy, DateTimeOffset ApprovedAt, bool Used = false);
public sealed record BackupSourceOptions(string RootPath);
public sealed record OpsAuditRecord(string Id, string OperatorId, string Action, string Target, DateTimeOffset CreatedAt);

public sealed class OperationsService(
    JsonDocumentStore<TenantAccount> tenants,
    JsonDocumentStore<SupportGrantRecord> grants,
    JsonDocumentStore<BackupProfileRecord> backups,
    JsonDocumentStore<OpsAuditRecord> audits,
    JsonDocumentStore<BackupJobRecord> backupJobs,
    JsonDocumentStore<RestoreJobRecord> restoreJobs,
    IObjectStore objects,
    TimeProvider clock,
    BackupSourceOptions? sources = null,
    JsonDocumentStore<SupportGrantApprovalRecord>? supportApprovals = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim grantGate = new(1, 1);

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

    public Task<SupportGrantRecord> CreateGrantAsync(string tenantId, string operatorId, string scope, int minutes,
        CancellationToken cancellationToken) => CreateGrantAsync(tenantId, operatorId, scope, minutes, null, cancellationToken);

    public async Task<SupportGrantRecord> CreateGrantAsync(string tenantId, string operatorId, string scope, int minutes,
        string? approvalRef, CancellationToken cancellationToken)
    {
        if (await tenants.GetAsync(tenantId, cancellationToken) is null)
            throw new KeyNotFoundException("Tenant not found.");
        scope = string.IsNullOrWhiteSpace(scope) ? "read-only" : scope.Trim().ToLowerInvariant();
        if (scope is not ("read-only" or "diagnostics"))
            throw new InvalidOperationException("Support grant scope is not allowed.");
        if (minutes is < 1 or > 30) throw new InvalidOperationException("Support grant duration must be between 1 and 30 minutes.");
        var approval = await RequireTenantApprovalAsync(tenantId, scope, approvalRef, cancellationToken);
        var remainingMinutes = (int)Math.Floor((approval.ExpiresAt - clock.GetUtcNow()).TotalMinutes);
        if (remainingMinutes < 1) throw new InvalidOperationException("Tenant approval expires too soon to issue a grant.");
        var grant = new SupportGrantRecord(Guid.NewGuid().ToString("N"), tenantId, operatorId, scope,
            clock.GetUtcNow().AddMinutes(Math.Min(minutes, remainingMinutes)), "Pending")
        {
            ApprovalRef = approval.Reference
        };
        await grants.PutAsync(grant.Id, grant, cancellationToken);
        await AuditAsync(operatorId, "support_grant.created", tenantId, cancellationToken);
        return grant;
    }

    public async Task<SupportGrantApprovalRecord> IssueTenantApprovalAsync(string tenantId, string scope,
        string approvedBy, int minutes, CancellationToken cancellationToken)
    {
        if (await tenants.GetAsync(tenantId, cancellationToken) is null)
            throw new KeyNotFoundException("Tenant not found.");
        scope = string.IsNullOrWhiteSpace(scope) ? "read-only" : scope.Trim().ToLowerInvariant();
        if (scope is not ("read-only" or "diagnostics") || minutes is < 1 or > 30 || string.IsNullOrWhiteSpace(approvedBy))
            throw new InvalidOperationException("Tenant approval scope, duration and approver are invalid.");
        if (supportApprovals is null) throw new InvalidOperationException("Tenant approval storage is unavailable.");
        var now = clock.GetUtcNow();
        var approval = new SupportGrantApprovalRecord(Guid.NewGuid().ToString("N"), tenantId, scope,
            now.AddMinutes(minutes), approvedBy, now);
        await supportApprovals.PutAsync(approval.Reference, approval, cancellationToken);
        return approval;
    }

    public async Task<SupportGrantRecord> ApproveGrantAsync(string id, string operatorId, CancellationToken cancellationToken)
    {
        await grantGate.WaitAsync(cancellationToken);
        try
        {
            var grant = await grants.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Grant not found.");
            if (grant.Status != "Pending") throw new InvalidOperationException("Only pending grants can be approved.");
            if (string.IsNullOrWhiteSpace(operatorId) || operatorId.Equals(grant.OperatorId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A different authorized approver is required.");
            var approval = await RequireTenantApprovalAsync(grant.TenantId, grant.Scope, grant.ApprovalRef, cancellationToken);
            if (supportApprovals is null) throw new InvalidOperationException("Tenant approval storage is unavailable.");
            await supportApprovals.PutAsync(approval.Reference, approval with { Used = true }, cancellationToken);
            grant = grant with { Status = "Active", ApprovedBy = operatorId };
            await grants.PutAsync(id, grant, cancellationToken);
            await AuditAsync(operatorId, "support_grant.approved", id, cancellationToken);
            return grant;
        }
        finally { grantGate.Release(); }
    }

    private async Task<SupportGrantApprovalRecord> RequireTenantApprovalAsync(string tenantId, string scope,
        string? approvalRef, CancellationToken cancellationToken)
    {
        if (supportApprovals is null || string.IsNullOrWhiteSpace(approvalRef))
            throw new InvalidOperationException("A tenant-issued approval reference is required.");
        var approval = await supportApprovals.GetAsync(approvalRef, cancellationToken);
        if (approval is null || approval.Used || approval.ExpiresAt <= clock.GetUtcNow()
            || approval.TenantId != tenantId || approval.Scope != scope)
            throw new InvalidOperationException("Tenant approval is invalid, expired, consumed or does not match the requested scope.");
        return approval;
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

    public async Task SetBackupAsync(bool enabled, string operatorId, CancellationToken cancellationToken)
    {
        var profile = (await backups.GetAllAsync(cancellationToken)).FirstOrDefault()
            ?? throw new KeyNotFoundException("Backup profile not found.");
        await ConfigureBackupAsync(profile with { Enabled = enabled }, operatorId, cancellationToken);
    }

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
        var backupSetId = Guid.NewGuid();
        var jobs = new List<BackupJobRecord>();
        foreach (var component in components)
        {
            var job = new BackupJobRecord
            {
                ProfileId = profileId, BackupSetId = backupSetId, ProfileVersion = profile.Version, Component = component,
                Status = "Running", StartedAt = clock.GetUtcNow(),
                RetentionUntil = clock.GetUtcNow().AddDays(profile.RetentionDays)
            };
            try
            {
                var files = ComponentFiles(component);
                if (component != "rustfs" && files.Count == 0)
                    throw new InvalidOperationException($"No verified {component} snapshot is available; configure its backup connector.");
                var manifest = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    backup_id = job.Id, backup_set_id = backupSetId, profile_id = profileId, profile_version = profile.Version,
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
        var requiredComponents = EnabledComponents(profile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lastRestore = restores.Where(job => job.ProfileId == profileId && job.Status == "Verified" && job.Scope == "Full")
            .OrderByDescending(job => job.VerifiedAt).FirstOrDefault(job =>
            {
                var restoredJobs = jobs.Where(candidate => job.BackupIds.Contains(candidate.Id)).ToArray();
                var restoredComponents = restoredJobs.Select(candidate => candidate.Component).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return restoredJobs.Length > 0 && requiredComponents.IsSubsetOf(restoredComponents)
                    && restoredJobs.All(candidate => candidate.ProfileVersion == profile.Version);
            });
        var componentRto = components.ToDictionary(component => component, component => restores
            .Where(job => job.ProfileId == profileId && job.Status == "Verified" && job.VerifiedAt is not null)
            .OrderByDescending(job => job.VerifiedAt).FirstOrDefault(job =>
            {
                var restoredJobs = jobs.Where(candidate => job.BackupIds.Contains(candidate.Id)
                    && candidate.Component == component && candidate.ProfileVersion == profile.Version).ToArray();
                return restoredJobs.Length > 0;
            })?.RtoActualMinutes, StringComparer.OrdinalIgnoreCase);
        return new BackupCoverageDto(profileId, true, lastSuccessful is null ? "unverified" : "covered", components,
            lastSuccessful, lastSuccessful is null ? null : Math.Max(0, (int)Math.Ceiling((clock.GetUtcNow() - lastSuccessful.Value).TotalMinutes)),
            lastRestore?.RtoActualMinutes, lastSuccessful is null ? "unverified" : "measured",
            lastRestore is null ? "unverified" : "measured", componentRto);
    }

    public async Task<RestoreJobRecord> RequestRestoreAsync(RestoreRequest request, string operatorId,
        CancellationToken cancellationToken)
    {
        if (request.BackupIds.Count == 0 || string.IsNullOrWhiteSpace(operatorId)
            || request.Scope is not ("Full" or "Tenant"))
            throw new InvalidOperationException("Restore requires backups, scope and an authenticated requester.");
        var jobs = await backupJobs.GetAllAsync(cancellationToken);
        var selectedJobs = new List<BackupJobRecord>();
        foreach (var id in request.BackupIds.Distinct())
        {
            var job = jobs.FirstOrDefault(candidate => candidate.Id == id && candidate.Status == "Completed");
            if (job is null) throw new InvalidOperationException("A selected backup is missing or incomplete.");
            selectedJobs.Add(job);
        }
        var profileId = selectedJobs[0].ProfileId;
        if (selectedJobs.Any(job => job.ProfileId != profileId))
            throw new InvalidOperationException("A restore set cannot mix backup profiles.");
        if (selectedJobs.Any(job => !job.BackupSetId.HasValue)
            || selectedJobs.Select(job => job.BackupSetId).Distinct().Count() != 1)
            throw new InvalidOperationException("A restore request must select one completed, non-legacy backup set.");
        if (request.Scope == "Full")
        {
            var profile = await backups.GetAsync(profileId, cancellationToken)
                ?? throw new KeyNotFoundException("Backup profile not found.");
            var requiredComponents = EnabledComponents(profile).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedComponents = selectedJobs.Select(job => job.Component).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requiredComponents.Except(selectedComponents, StringComparer.OrdinalIgnoreCase).ToArray();
            if (requiredComponents.Count == 0 || missing.Length > 0)
                throw new InvalidOperationException($"Full restore requires every profile component. Missing: {string.Join(", ", missing)}.");
        }
        var restore = new RestoreJobRecord
        {
            ProfileId = profileId, BackupIds = selectedJobs.Select(job => job.Id).ToArray(), Scope = request.Scope,
            ApprovalRef = $"restore:{Guid.NewGuid():N}", Requester = operatorId, RequestedAt = clock.GetUtcNow()
        };
        await restoreJobs.PutAsync(restore.Id.ToString(), restore, cancellationToken);
        await AuditAsync(operatorId, "backup.restore_requested", restore.Id.ToString(), cancellationToken);
        return restore;
    }

    public async Task<RestoreJobRecord> ApproveRestoreAsync(Guid id, RestoreApprovalRequest request,
        string operatorId, CancellationToken cancellationToken)
    {
        var restore = await restoreJobs.GetAsync(id.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Restore job not found.");
        if (restore.Status != "PendingApproval") throw new InvalidOperationException("Restore is not pending approval.");
        if (string.IsNullOrWhiteSpace(operatorId) || string.IsNullOrWhiteSpace(request.Resolution)
            || operatorId.Equals(restore.Requester, StringComparison.OrdinalIgnoreCase))
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
        restore.Approver = operatorId;
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
        await AuditAsync(operatorId, "backup.restore_verified", restore.Id.ToString(), cancellationToken);
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
            throw new InvalidOperationException("Retention must be between 0 and 3650 days; enabled profiles require at least 1 day.");
        if (!profile.Enabled) return;
        if (profile.PostgresPitr && !profile.PostgresFull) throw new InvalidOperationException("PITR requires Postgres full backup.");
        if (profile.Offsite && !(profile.PostgresFull || profile.RustFs || profile.KeyVault)) throw new InvalidOperationException("Offsite requires a local backup component.");
        if (profile.RestoreDrills && EnabledComponents(profile).Count == 0) throw new InvalidOperationException("Restore drills require a recoverable component.");
        if (profile.Offsite && string.IsNullOrWhiteSpace(profile.TargetRef)) throw new InvalidOperationException("Offsite requires an explicit target reference.");
    }

    private List<object> ComponentFiles(string component)
    {
        var root = sources?.RootPath ?? "data";
        var objectRoot = Path.Combine(root, "objects");
        if (component == "rustfs" && !Directory.Exists(objectRoot))
            throw new InvalidOperationException("RustFS source is not configured; an explicitly verified object store is required.");
        var paths = component switch
        {
            "postgres" => Directory.Exists(root) ? Directory.GetFiles(root, "*.db", SearchOption.AllDirectories) : [],
            "postgres_pitr" => Directory.Exists(root) ? Directory.GetFiles(root, "*wal*", SearchOption.AllDirectories) : [],
            "rustfs" => Directory.GetFiles(objectRoot, "*", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFileName(path), ".snapshot-verified", StringComparison.Ordinal)).ToArray(),
            "keyvault" => File.Exists(Path.Combine(root, "keyvault.json")) ? [Path.Combine(root, "keyvault.json")] : [],
            _ => []
        };
        if (component == "rustfs" && paths.Length == 0 && !File.Exists(Path.Combine(objectRoot, ".snapshot-verified")))
            throw new InvalidOperationException("An empty RustFS store requires an explicit verified-snapshot marker.");
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
