using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Governance;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class DataLifecycleServiceTests
{
    [Fact]
    public async Task Archive_restore_verifies_checksum_and_rights_gate_unknown_sources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var service = new DataLifecycleService(
                new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")),
                objects, db, TimeProvider.System, new TenantPurgeService(db, objects, []));
            var tenantId = Guid.NewGuid();
            var archive = await service.ArchiveAsync(tenantId, "2026-11", "Archive", 42, CancellationToken.None);
            var restored = await service.RestoreArchiveAsync(tenantId, archive.Id, CancellationToken.None);
            var rights = await service.RecordRightsAsync(tenantId, new RightsRecord
            {
                ArtifactRef = "artifact:unknown", ThirdPartyRights = "Unknown", SourceRefs = []
            }, CancellationToken.None);

            Assert.Equal("Restored", restored.Status);
            Assert.Equal("Blocked", rights.PublishStatus);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Legal_hold_blocks_purge_until_released()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var clock = new MutableTimeProvider();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var moduleStore = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "memory.json"));
            await moduleStore.PutAsync("memory-1", new MemoryClaim("memory-1", tenantId.ToString(), "company", "purge me", "source", 1, "trusted", "Active", 1), CancellationToken.None);
            var service = new DataLifecycleService(
                new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")),
                objects, db, clock, new TenantPurgeService(db, objects, [moduleStore]));
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
            var hold = await service.CreateLegalHoldAsync(tenantId, new LegalHoldRecord
            {
                ResourceRef = tenantId.ToString(), Reason = "Investigation", AuthorizedBy = "Operator"
            }, CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            await service.ReleaseLegalHoldAsync(tenantId, hold.Id, CancellationToken.None);
            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);

            Assert.NotEmpty(proof.ProofHash);
            Assert.False(await db.Tenants.AnyAsync(x => x.Id == tenantId));
            Assert.Null(await moduleStore.GetAsync("memory-1", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
