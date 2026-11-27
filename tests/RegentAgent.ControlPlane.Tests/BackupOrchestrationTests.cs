using RegentAgent.Contracts.Operations;
using RegentAgent.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class BackupOrchestrationTests
{
    [Fact]
    public async Task Disabled_profile_has_no_false_rpo_or_rto()
    {
        var root = NewRoot();
        try
        {
            var service = CreateService(root);
            await service.SeedAsync(CancellationToken.None);
            var coverage = await service.GetCoverageAsync("default", CancellationToken.None);
            Assert.False(coverage.Enabled);
            Assert.Equal("not_configured", coverage.Status);
            Assert.Null(coverage.EffectiveRpoMinutes);
            Assert.Null(coverage.EffectiveRtoMinutes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunBackupAsync("default", "SRE", CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Component_dependencies_and_two_person_restore_are_enforced()
    {
        var root = NewRoot();
        try
        {
            var service = CreateService(root);
            await service.SeedAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfigureBackupAsync(
                new BackupProfileRecord("default", "local", true, "pitr", 35) { PostgresPitr = true }, "SRE", CancellationToken.None));
            var profile = await service.ConfigureBackupAsync(new BackupProfileRecord("default", "local", true,
                "rustfs", 35) { RustFs = true, RestoreDrills = true }, "SRE", CancellationToken.None);
            Assert.Equal(2, profile.Version);
            var jobs = await service.RunBackupAsync("default", "SRE", CancellationToken.None);
            var job = Assert.Single(jobs);
            Assert.Equal("Completed", job.Status);
            var coverage = await service.GetCoverageAsync("default", CancellationToken.None);
            Assert.NotNull(coverage.EffectiveRpoMinutes);
            Assert.Equal("unverified", coverage.RtoStatus);
            var restore = await service.RequestRestoreAsync(new RestoreRequest([job.Id], "Full"), "SRE One", CancellationToken.None);
            Assert.Equal("SRE One", restore.Requester);
            Assert.StartsWith("restore:", restore.ApprovalRef);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveRestoreAsync(restore.Id,
                new RestoreApprovalRequest("Self approval"), "SRE One", CancellationToken.None));
            var verified = await service.ApproveRestoreAsync(restore.Id,
                new RestoreApprovalRequest("Manifest hashes verified"), "SRE Two", CancellationToken.None);
            Assert.Equal("SRE Two", verified.Approver);
            Assert.Equal("Verified", verified.Status);
            Assert.NotNull(verified.RpoActualMinutes);
            Assert.NotNull(verified.RtoActualMinutes);
            Assert.NotEmpty(verified.ReportRef);
            Assert.Equal("measured", (await service.GetCoverageAsync("default", CancellationToken.None)).RtoStatus);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Legacy_jobs_and_partial_tenant_restores_do_not_count_as_full_recovery_coverage()
    {
        var root = NewRoot();
        try
        {
            var service = CreateService(root);
            await service.SeedAsync(CancellationToken.None);
            var legacy = new BackupJobRecord
            {
                ProfileId = "default", Component = "rustfs", Status = "Completed",
                StartedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow
            };
            await new JsonDocumentStore<BackupJobRecord>(Path.Combine(root, "jobs.json"))
                .PutAsync(legacy.Id.ToString(), legacy, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RequestRestoreAsync(
                new RestoreRequest([legacy.Id], "Tenant"), "SRE One", CancellationToken.None));

            File.WriteAllText(Path.Combine(root, "source", "keyvault.json"), "{}");
            await service.ConfigureBackupAsync(new BackupProfileRecord("default", "local", true, "rustfs,keyvault", 35)
            {
                RustFs = true, KeyVault = true, RestoreDrills = true
            }, "SRE", CancellationToken.None);
            var jobs = await service.RunBackupAsync("default", "SRE", CancellationToken.None);
            var rustfs = jobs.Single(job => job.Component == "rustfs");
            var restore = await service.RequestRestoreAsync(new RestoreRequest([rustfs.Id], "Tenant"), "SRE One", CancellationToken.None);
            await service.ApproveRestoreAsync(restore.Id, new RestoreApprovalRequest("Partial drill"), "SRE Two", CancellationToken.None);

            var coverage = await service.GetCoverageAsync("default", CancellationToken.None);
            Assert.Null(coverage.EffectiveRtoMinutes);
            Assert.Equal("unverified", coverage.RtoStatus);
            Assert.True(coverage.ComponentRtoMinutes?.ContainsKey("keyvault"));
            Assert.NotNull(coverage.ComponentRtoMinutes?.GetValueOrDefault("rustfs"));
            Assert.Null(coverage.ComponentRtoMinutes?.GetValueOrDefault("keyvault"));
        }
        finally { Delete(root); }
    }

    private static OperationsService CreateService(string root) => new(
        new JsonDocumentStore<TenantAccount>(Path.Combine(root, "tenants.json")),
        new JsonDocumentStore<SupportGrantRecord>(Path.Combine(root, "grants.json")),
        new JsonDocumentStore<BackupProfileRecord>(Path.Combine(root, "profiles.json")),
        new JsonDocumentStore<OpsAuditRecord>(Path.Combine(root, "audit.json")),
        new JsonDocumentStore<BackupJobRecord>(Path.Combine(root, "jobs.json")),
        new JsonDocumentStore<RestoreJobRecord>(Path.Combine(root, "restores.json")),
        new LocalFileObjectStore(Path.Combine(root, "objects")), TimeProvider.System,
        sources: new BackupSourceOptions(Path.Combine(root, "source")));

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"backup-orchestration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "source", "objects"));
        File.WriteAllText(Path.Combine(root, "source", "objects", ".snapshot-verified"), "verified");
        return root;
    }
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
