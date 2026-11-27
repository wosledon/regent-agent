using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
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
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var service = new DataLifecycleService(
                new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")),
                objects, db, TimeProvider.System, new TenantPurgeService(db, objects, [],
                    new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json"))), exports),
                new TenantRlsScope(db));
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
    public async Task Tenant_soft_delete_accepts_lowercase_guid_and_restore_reactivates_it()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-soft-delete-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "soft-delete", CompanyName = "Soft Delete Co" });
            await db.SaveChangesAsync();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var service = new DataLifecycleService(
                new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")),
                objects, db, TimeProvider.System,
                new TenantPurgeService(db, objects, [],
                    new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json"))), exports),
                new TenantRlsScope(db));

            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString("D"), "Owner requested", CancellationToken.None);
            Assert.Equal("SoftDeleted", (await db.Tenants.SingleAsync()).Status);
            await service.RestoreAsync(tenantId, request.Id, CancellationToken.None);
            Assert.Equal("Active", (await db.Tenants.SingleAsync()).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Project_purge_preserves_tenant_and_canary_data_without_tenant_proof()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-project-purge-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var otherTenantId = Guid.NewGuid();
            db.Tenants.AddRange(
                new TenantRecord { Id = tenantId, Name = "project-purge", CompanyName = "Project Purge Co" },
                new TenantRecord { Id = otherTenantId, Name = "retained", CompanyName = "Retained Co" });
            await db.SaveChangesAsync();
            await SeedRelationalCanaryAsync(db, tenantId, "target");
            await SeedRelationalCanaryAsync(db, otherTenantId, "other");
            var projectId = await db.Projects.Where(item => item.TenantId == tenantId).Select(item => item.Id).SingleAsync();
            var clock = new MutableTimeProvider();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json"));
            var exportStore = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var moduleStore = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "memory.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var targetObjectKey = $"tenants/{tenantId}/misc/canary";
            var otherObjectKey = $"tenants/{otherTenantId}/misc/canary";
            foreach (var key in new[] { targetObjectKey, otherObjectKey })
            {
                await using var content = new MemoryStream("retain"u8.ToArray());
                await objects.PutAsync(new ObjectUploadRequest(key, content, "text/plain"), CancellationToken.None);
            }
            foreach (var (id, ownerTenantId) in new[] { ("target-memory", tenantId), ("other-memory", otherTenantId) })
            {
                await moduleStore.PutAsync(id, new MemoryClaim(id, ownerTenantId.ToString(), "company", "retain", "source", 1,
                    "trusted", "Active", 1), CancellationToken.None);
                await vectors.PutAsync(new MemoryVectorRecord(id, ownerTenantId.ToString(), "hash", "test-v1",
                    LocalEmbeddingProvider.Embed("retain"), clock.GetUtcNow()), CancellationToken.None);
            }
            var purge = new TenantPurgeService(db, objects, [moduleStore, exportStore], vectors, exportStore);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var request = await service.SoftDeleteAsync(tenantId, "Project", projectId.ToString(), "Project removed",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(31));

            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);

            Assert.Equal("Project", proof.ResourceType);
            Assert.Equal(projectId.ToString(), proof.ResourceId);
            var lifecycle = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(lifecycle);
            Assert.False(lifecycle.IsPurged);
            Assert.Equal(proof.Id, Assert.Single(lifecycle.DeletionProofs).Id);
            Assert.DoesNotContain(lifecycle.DeletionProofs, item =>
                item.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Active", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            Assert.True(await db.Tenants.AnyAsync(item => item.Id == otherTenantId));
            Assert.Single(await db.Projects.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Milestones.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Tasks.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Employees.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Memberships.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.WorkEntries.Where(item => item.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Projects.Where(item => item.TenantId == otherTenantId).ToListAsync());
            Assert.NotNull(await moduleStore.GetAsync("target-memory", CancellationToken.None));
            Assert.NotNull(await moduleStore.GetAsync("other-memory", CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(tenantId.ToString(), CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(otherTenantId.ToString(), CancellationToken.None));
            Assert.NotNull(await objects.GetMetadataAsync(targetObjectKey, CancellationToken.None));
            Assert.NotNull(await objects.GetMetadataAsync(otherObjectKey, CancellationToken.None));
            Assert.False(TenantPurgeFence.IsPurging(tenantId));
            using var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
            Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Due_tenant_purge_consumes_duplicate_requests_with_one_minimal_tombstone()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-due-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "due-purge", CompanyName = "Due Purge Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json"));
            var exportStore = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [exportStore], vectors, exportStore);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "First request", CancellationToken.None);
            await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Duplicate request", CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));

            var purged = await service.RunDuePurgeAsync(CancellationToken.None);

            Assert.Equal(1, purged);
            var lifecycle = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(lifecycle);
            Assert.Single(lifecycle.DeletionProofs);
            Assert.Empty(lifecycle.DeletionRequests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Purge_blocks_concurrent_lifecycle_writes_and_keeps_tombstone_proofs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lifecycle-race-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "purge-race", CompanyName = "Purge Race Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecyclePath = Path.Combine(root, "lifecycle.json");
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(lifecyclePath);
            var exportStore = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var objects = new BlockingObjectStore(new LocalFileObjectStore(Path.Combine(root, "objects")));
            var purge = new TenantPurgeService(db, objects, [exportStore, lifecycleStore], vectors, exportStore);
            var purgeService = new DataLifecycleService(new JsonDocumentStore<DataLifecycleState>(lifecyclePath), objects, db,
                clock, purge, new TenantRlsScope(db));
            var writerService = new DataLifecycleService(new JsonDocumentStore<DataLifecycleState>(lifecyclePath), objects, db,
                clock, purge, new TenantRlsScope(db));
            var request = await writerService.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
            await writerService.CreateRetentionPolicyAsync(tenantId, new RetentionPolicyRecord
            {
                DataClass = "BusinessRecord", HotDays = 1, ArchiveDays = 2, ColdDays = 3, DeleteAfterDays = 4
            }, CancellationToken.None);
            var archive = await writerService.ArchiveAsync(tenantId, "2026-11", "Archive", 42, CancellationToken.None);
            await writerService.RecordRightsAsync(tenantId, new RightsRecord
            {
                ArtifactRef = archive.ObjectKey, ThirdPartyRights = "Cleared", SourceRefs = ["source-1"]
            }, CancellationToken.None);
            await writerService.RecordLicenseAsync(tenantId, new LicenseRecord
            {
                ArtifactRef = archive.ObjectKey, License = "MIT", NoticeRef = "notice-1", SbomRef = "sbom-1",
                ContributorConfirmed = true, TrademarkReviewed = true
            }, CancellationToken.None);
            var hold = await writerService.CreateLegalHoldAsync(tenantId, new LegalHoldRecord
            {
                ResourceRef = tenantId.ToString(), Reason = "Investigation", AuthorizedBy = "Operator"
            }, CancellationToken.None);
            await writerService.ReleaseLegalHoldAsync(tenantId, hold.Id, CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));

            var purgeTask = purgeService.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
            await objects.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task[] blockedWrites =
            [
                writerService.ArchiveAsync(tenantId, "2026-12", "Archive", 1, CancellationToken.None),
                writerService.CreateLegalHoldAsync(tenantId, new LegalHoldRecord
                {
                    ResourceRef = "*", Reason = "Late hold", AuthorizedBy = "Operator"
                }, CancellationToken.None),
                writerService.CreateRetentionPolicyAsync(tenantId, new RetentionPolicyRecord
                {
                    DataClass = "LatePolicy", HotDays = 1, ArchiveDays = 2, ColdDays = 3, DeleteAfterDays = 4
                }, CancellationToken.None),
                writerService.RecordRightsAsync(tenantId, new RightsRecord
                {
                    ArtifactRef = "late", ThirdPartyRights = "Cleared", SourceRefs = ["late"]
                }, CancellationToken.None),
                writerService.RecordLicenseAsync(tenantId, new LicenseRecord
                {
                    ArtifactRef = "late", License = "MIT", NoticeRef = "late", SbomRef = "late",
                    ContributorConfirmed = true, TrademarkReviewed = true
                }, CancellationToken.None),
                writerService.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Late request", CancellationToken.None)
            ];
            objects.AllowDelete.TrySetResult();

            var proof = await purgeTask;
            foreach (var write in blockedWrites)
                await Assert.ThrowsAnyAsync<InvalidOperationException>(async () => await write);

            var lifecycle = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(lifecycle);
            Assert.True(lifecycle.IsPurged);
            Assert.Equal(proof.PurgedAt, lifecycle.PurgedAt);
            Assert.Equal(proof.Id, Assert.Single(lifecycle.DeletionProofs).Id);
            Assert.Empty(lifecycle.DeletionRequests);
            Assert.Empty(lifecycle.LegalHolds);
            Assert.Empty(lifecycle.RetentionPolicies);
            Assert.Empty(lifecycle.Archives);
            Assert.Empty(lifecycle.Backups);
            Assert.Empty(lifecycle.Rights);
            Assert.Empty(lifecycle.Licenses);
            Assert.Null(await objects.GetMetadataAsync(archive.ObjectKey, CancellationToken.None));
            var archiveDirectory = Path.Combine(root, "objects", "tenants", tenantId.ToString(), "archives");
            if (Directory.Exists(archiveDirectory))
                Assert.Empty(Directory.EnumerateFiles(archiveDirectory, "*", SearchOption.AllDirectories));
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
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "legal-hold", CompanyName = "Legal Hold Co" });
            await db.SaveChangesAsync();
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json"));
            var exportStore = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var moduleStore = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "memory.json"));
            await moduleStore.PutAsync("memory-1", new MemoryClaim("memory-1", tenantId.ToString(), "company", "purge me", "source", 1, "trusted", "Active", 1), CancellationToken.None);
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            await vectors.PutAsync(new MemoryVectorRecord("memory-1", tenantId.ToString(), "hash", "test-v1",
                LocalEmbeddingProvider.Embed("purge me"), DateTimeOffset.UtcNow), CancellationToken.None);
            var otherTenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = otherTenantId, Name = "retain", CompanyName = "Retain Co" });
            await db.SaveChangesAsync();
            await SeedRelationalCanaryAsync(db, tenantId, "target");
            await SeedRelationalCanaryAsync(db, otherTenantId, "other");
            await moduleStore.PutAsync("memory-2", new MemoryClaim("memory-2", otherTenantId.ToString(), "company", "retain", "source", 1, "trusted", "Active", 1), CancellationToken.None);
            await vectors.PutAsync(new MemoryVectorRecord("memory-2", otherTenantId.ToString(), "hash", "test-v1",
                LocalEmbeddingProvider.Embed("retain"), DateTimeOffset.UtcNow), CancellationToken.None);
            var retainedObjectKey = $"tenants/{otherTenantId}/exports/retained.zip";
            await using (var retained = new MemoryStream("retain"u8.ToArray()))
                await objects.PutAsync(new ObjectUploadRequest(retainedObjectKey, retained, "application/zip"), CancellationToken.None);
            var purge = new TenantPurgeService(db, objects, [moduleStore, exportStore], vectors, exportStore);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
            var hold = await service.CreateLegalHoldAsync(tenantId, new LegalHoldRecord
            {
                ResourceRef = tenantId.ToString(), Reason = "Investigation", AuthorizedBy = "Operator"
            }, CancellationToken.None);
            await service.CreateRetentionPolicyAsync(tenantId, new RetentionPolicyRecord
            {
                DataClass = "BusinessRecord", HotDays = 1, ArchiveDays = 2, ColdDays = 3, DeleteAfterDays = 4
            }, CancellationToken.None);
            var archive = await service.ArchiveAsync(tenantId, "2026-11", "Archive", 42, CancellationToken.None);
            await service.RecordRightsAsync(tenantId, new RightsRecord
            {
                ArtifactRef = archive.ObjectKey, ThirdPartyRights = "Cleared", SourceRefs = ["source-1"]
            }, CancellationToken.None);
            await service.RecordLicenseAsync(tenantId, new LicenseRecord
            {
                ArtifactRef = archive.ObjectKey, License = "MIT", NoticeRef = "notice-1", SbomRef = "sbom-1",
                ContributorConfirmed = true, TrademarkReviewed = true
            }, CancellationToken.None);
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Key = "purge-key", Operation = "purge-test",
                RequestHash = "hash", ResponseJson = "null", CreatedAt = clock.GetUtcNow()
            });
            db.Outbox.Add(new OutboxRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventType = "purge.test", PayloadJson = "{}",
                CreatedAt = clock.GetUtcNow()
            });
            db.Inbox.Add(new InboxRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, MessageId = Guid.NewGuid(), EventType = "purge.test",
                PayloadJson = "{}", CreatedAt = clock.GetUtcNow()
            });
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, Key = "retain-key", Operation = "purge-test",
                RequestHash = "hash", ResponseJson = "null", CreatedAt = clock.GetUtcNow()
            });
            db.Outbox.Add(new OutboxRecord
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, EventType = "purge.retain", PayloadJson = "{}",
                CreatedAt = clock.GetUtcNow()
            });
            db.Inbox.Add(new InboxRecord
            {
                Id = Guid.NewGuid(), TenantId = otherTenantId, MessageId = Guid.NewGuid(), EventType = "purge.retain",
                PayloadJson = "{}", CreatedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync();
            clock.Advance(TimeSpan.FromDays(8));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            await service.ReleaseLegalHoldAsync(tenantId, hold.Id, CancellationToken.None);
            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);

            Assert.NotEmpty(proof.ProofHash);
            Assert.False(await db.Tenants.AnyAsync(x => x.Id == tenantId));
            Assert.True(await db.Tenants.AnyAsync(x => x.Id == otherTenantId));
            Assert.Empty(await db.Projects.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Milestones.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Tasks.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Employees.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Memberships.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.WorkEntries.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.Projects.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Milestones.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Tasks.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Employees.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Memberships.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.WorkEntries.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Empty(await db.IdempotencyRecords.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Outbox.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Empty(await db.Inbox.Where(x => x.TenantId == tenantId).ToListAsync());
            Assert.Single(await db.IdempotencyRecords.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Outbox.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Single(await db.Inbox.Where(x => x.TenantId == otherTenantId).ToListAsync());
            Assert.Null(await moduleStore.GetAsync("memory-1", CancellationToken.None));
            Assert.NotNull(await moduleStore.GetAsync("memory-2", CancellationToken.None));
            Assert.Empty(await vectors.GetAllAsync(tenantId.ToString(), CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(otherTenantId.ToString(), CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(archive.ObjectKey, CancellationToken.None));
            Assert.NotNull(await objects.GetMetadataAsync(retainedObjectKey, CancellationToken.None));
            var lifecycle = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(lifecycle);
            Assert.Equal(proof.Id, Assert.Single(lifecycle.DeletionProofs).Id);
            Assert.Empty(lifecycle.DeletionRequests);
            Assert.Empty(lifecycle.LegalHolds);
            Assert.Empty(lifecycle.RetentionPolicies);
            Assert.Empty(lifecycle.Archives);
            Assert.Empty(lifecycle.Backups);
            Assert.Empty(lifecycle.Rights);
            Assert.Empty(lifecycle.Licenses);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task SeedRelationalCanaryAsync(RegentDbContext db, Guid tenantId, string name)
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var milestoneId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        db.Users.Add(new UserRecord
        {
            Id = userId, Email = $"{name}-{userId:N}@example.test", DisplayName = $"{name} owner"
        });
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = "Owner"
        });
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"{name}-{employeeId:N}", DisplayName = $"{name} employee"
        });
        db.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = $"{name} project", Mission = "Synthetic",
            Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
            TargetAt = now, UpdatedAt = now
        });
        db.Milestones.Add(new MilestoneRecord
        {
            Id = milestoneId, TenantId = tenantId, ProjectId = projectId, Name = $"{name} milestone",
            TargetAt = now, Status = "Pending", AcceptanceRule = "Synthetic"
        });
        db.Tasks.Add(new TaskRecord
        {
            Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = $"{name} task",
            TaskType = "work", Status = "Pending", Assignee = "Employee", UpdatedAt = now
        });
        db.WorkEntries.Add(new WorkEntryRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, TaskId = taskId,
            OccurredAt = now, Title = $"{name} work", EventType = "synthetic", Status = "Recorded"
        });
        await db.SaveChangesAsync();
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class BlockingObjectStore(IObjectStore inner) : IObjectStore
    {
        private int deleteCalls;
        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref deleteCalls) != 1) return await inner.DeletePrefixAsync(prefix, cancellationToken);
            DeleteStarted.TrySetResult();
            await AllowDelete.Task.WaitAsync(cancellationToken);
            return await inner.DeletePrefixAsync(prefix, cancellationToken);
        }
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);

        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
