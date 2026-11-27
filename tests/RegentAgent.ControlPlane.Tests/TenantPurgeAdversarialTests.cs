using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.Agents;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Agents;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Governance;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TenantPurgeAdversarialTests
{
    [Fact]
    public async Task Purge_sweeps_all_tenant_object_namespaces_and_untracked_orphans()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var otherTenantId = Guid.NewGuid();
            db.Tenants.AddRange(
                new TenantRecord { Id = tenantId, Name = "purge", CompanyName = "Purge Co" },
                new TenantRecord { Id = otherTenantId, Name = "retain", CompanyName = "Retain Co" });
            await db.SaveChangesAsync();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            string[] keys =
            [
                $"tenants/{tenantId}/archives/2026/archive/a.json",
                $"tenants/{tenantId}/backups/b.json",
                $"tenants/{tenantId}/projects/p/artifacts/a",
                $"tenants/{tenantId}/exports/e.zip",
                $"tenants/{tenantId}/misc/untracked-orphan"
            ];
            foreach (var key in keys)
            {
                await using var stream = new MemoryStream("orphan"u8.ToArray());
                await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/octet-stream"), CancellationToken.None);
            }
            var retainedKey = $"tenants/{otherTenantId}/exports/retained.zip";
            await using (var retained = new MemoryStream("retain"u8.ToArray()))
                await objects.PutAsync(new ObjectUploadRequest(retainedKey, retained, "application/zip"), CancellationToken.None);
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports);

            await purge.PurgeAsync(tenantId, CancellationToken.None);

            foreach (var key in keys) Assert.Null(await objects.GetMetadataAsync(key, CancellationToken.None));
            Assert.NotNull(await objects.GetMetadataAsync(retainedKey, CancellationToken.None));
            Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId}/", CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Pending_upload_finishes_before_namespace_delete_and_late_upload_is_rejected()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "upload", CompanyName = "Upload Co" });
            await db.SaveChangesAsync();
            var inner = new BlockingPutObjectStore(new LocalFileObjectStore(Path.Combine(root, "objects")));
            var uploads = new TenantObjectUploadTracker();
            var objects = new TenantPurgeObjectStore(inner, uploads);
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, uploads: uploads);
            var key = $"tenants/{tenantId}/artifacts/pending";
            await using var content = new MemoryStream("pending"u8.ToArray());

            var uploadTask = objects.PutAsync(new ObjectUploadRequest(key, content, "text/plain"), CancellationToken.None);
            await inner.PutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var purgeTask = purge.PurgeAsync(tenantId, CancellationToken.None);
            await WaitUntilAsync(() => Task.FromResult(TenantPurgeFence.IsPurging(tenantId)));
            Assert.False(purgeTask.IsCompleted);
            inner.AllowPut.TrySetResult();
            await uploadTask;
            await purgeTask;

            Assert.Null(await objects.GetMetadataAsync(key, CancellationToken.None));
            Assert.Equal(0, uploads.GetPendingCount(tenantId));
            await using var lateContent = new MemoryStream("late"u8.ToArray());
            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() =>
                objects.PutAsync(new ObjectUploadRequest(key, lateContent, "text/plain"), CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task In_flight_agent_context_write_finishes_before_purge_and_late_write_is_rejected()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "runtime", CompanyName = "Runtime Co" });
            await db.SaveChangesAsync();
            var contexts = new BlockingContextStore();
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [contexts], vectors, exports);
            var runtime = new AgentRuntimeService(contexts, TimeProvider.System);
            var taskId = Guid.NewGuid();

            var buildTask = runtime.BuildContextAsync(tenantId, taskId, "system", "memory", "{}", [], "{}", false,
                CancellationToken.None);
            await contexts.PutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var purgeTask = purge.PurgeAsync(tenantId, CancellationToken.None);
            await WaitUntilAsync(() => Task.FromResult(TenantPurgeFence.IsPurging(tenantId)));
            Assert.False(purgeTask.IsCompleted);
            contexts.AllowPut.TrySetResult();
            await buildTask;
            await purgeTask;

            Assert.Empty(await contexts.GetAllAsync(CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() =>
                runtime.BuildContextAsync(tenantId, Guid.NewGuid(), "system", "memory", "{}", [], "{}", false,
                    CancellationToken.None));
            Assert.Equal(1, contexts.PutCalls);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Purge_recovers_from_object_and_json_failures_after_relational_delete()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "recovery", CompanyName = "Recovery Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json"));
            var objects = new FaultingObjectStore(new LocalFileObjectStore(Path.Combine(root, "objects")))
            {
                FailDeletePrefix = true
            };
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var jsonStore = new FaultingTenantJsonStore { FailDeleteTenant = true };
            var purge = new TenantPurgeService(db, objects, [jsonStore], vectors, exports, lifecycleStore, clock);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var archive = await service.ArchiveAsync(tenantId, "2026-11", "Archive", 1, CancellationToken.None);
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            var afterObjectFailure = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(afterObjectFailure!.IsPurging);
            Assert.Empty(afterObjectFailure.DeletionProofs);

            objects.FailDeletePrefix = false;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId}/", CancellationToken.None));
            var afterJsonFailure = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(afterJsonFailure!.IsPurging);
            Assert.Empty(afterJsonFailure.DeletionProofs);

            jsonStore.FailDeleteTenant = false;
            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
            var completed = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(completed!.IsPurged);
            Assert.Equal(proof.Id, Assert.Single(completed.DeletionProofs).Id);
            Assert.Null(await objects.GetMetadataAsync(archive.ObjectKey, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Purge_persists_purging_tombstone_before_relational_or_object_deletion()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "ordering", CompanyName = "Ordering Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var archive = await service.ArchiveAsync(tenantId, "2026-11", "Archive", 1, CancellationToken.None);
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));
            lifecycleStore.AfterSave = state =>
            {
                if (!state.IsPurging) return Task.CompletedTask;
                lifecycleStore.PurgingSaved.TrySetResult();
                return lifecycleStore.AllowPurge.Task;
            };

            var purgeTask = service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
            await lifecycleStore.PurgingSaved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await db.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            Assert.NotNull(await objects.GetMetadataAsync(archive.ObjectKey, CancellationToken.None));
            var pending = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(pending!.IsPurging);
            Assert.Empty(pending.DeletionProofs);
            lifecycleStore.AllowPurge.TrySetResult();

            var proof = await purgeTask;
            Assert.Equal(proof.Id, Assert.Single((await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None))!.DeletionProofs).Id);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Crash_after_tombstone_before_tenant_status_blocks_writes_and_resumes_to_proof()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        var connectionString = $"Data Source={Path.Combine(root, "regent.db")}";
        var lifecyclePath = Path.Combine(root, "data", "modules", "lifecycle.json");
        var clock = new MutableTimeProvider();
        var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
        var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
        var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
        var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(lifecyclePath));
        try
        {
            await using (var db = CreateDb(connectionString))
            {
                await db.Database.EnsureCreatedAsync();
                db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "crash-window", CompanyName = "Crash Window Co" });
                await db.SaveChangesAsync();
                lifecycleStore.AfterSave = state =>
                {
                    if (!state.IsPurging) return Task.CompletedTask;
                    lifecycleStore.AfterSave = null;
                    return Task.FromException(new InvalidOperationException("Simulated crash after tombstone commit."));
                };
                var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);

                await Assert.ThrowsAsync<InvalidOperationException>(() => purge.PurgeAsync(tenantId, CancellationToken.None));

                Assert.Equal("Active", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
                var pending = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
                Assert.NotNull(pending);
                Assert.True(pending.IsPurging);
                Assert.Empty(pending.DeletionProofs);
            }

            TenantPurgeFence.Reset(tenantId);
            var services = new ServiceCollection();
            services.AddDbContext<RegentDbContext>(options => options.UseSqlite(connectionString));
            services.AddSingleton<ITenantPurgeStateProvider>(provider => new PersistedTenantPurgeStateProvider(
                provider.GetRequiredService<IServiceScopeFactory>(), lifecyclePath));
            await using var serviceProvider = services.BuildServiceProvider();
            var purgeState = serviceProvider.GetRequiredService<ITenantPurgeStateProvider>();
            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() =>
                TenantPurgeFence.CheckPersistedPurgeAsync(tenantId, CancellationToken.None, purgeState));
            TenantPurgeFence.Reset(tenantId);

            await using var scope = serviceProvider.CreateAsyncScope();
            var resumeDb = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            var resumePurge = new TenantPurgeService(resumeDb, objects, [], vectors, exports, lifecycleStore, clock);
            var lifecycle = new DataLifecycleService(lifecycleStore, objects, resumeDb, clock, resumePurge,
                new TenantRlsScope(resumeDb));
            var requestId = (await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None))!
                .DeletionRequests.Single().Id;

            var proof = await lifecycle.PurgeAsync(tenantId, requestId, "PURGE", CancellationToken.None);

            Assert.False(await resumeDb.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            var completed = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(completed!.IsPurged);
            Assert.Equal(proof.Id, Assert.Single(completed.DeletionProofs).Id);
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Cancellation_before_purging_tombstone_commit_releases_write_admission()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        try
        {
            await using var db = CreateDb();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "cancel-purge", CompanyName = "Cancel Purge Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));
            using var cancellation = new CancellationTokenSource();
            lifecycleStore.BeforeSave = state =>
            {
                if (!state.IsPurging) return Task.CompletedTask;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", cancellation.Token));

            lifecycleStore.BeforeSave = null;
            db.ChangeTracker.Clear();
            Assert.Equal("SoftDeleted", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            var persisted = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.False(persisted.IsPurging);
            Assert.False(persisted.IsPurged);
            Assert.Equal("SoftDeleted", Assert.Single(persisted.DeletionRequests).Status);
            Assert.False(TenantPurgeFence.IsPurging(tenantId));
            using (var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None))
                Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));

            await service.CreateRetentionPolicyAsync(tenantId, new RetentionPolicyRecord
            {
                DataClass = "BusinessRecord", HotDays = 1, ArchiveDays = 2, ColdDays = 3, DeleteAfterDays = 4
            }, CancellationToken.None);
            var resumed = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(resumed);
            Assert.Equal("BusinessRecord", Assert.Single(resumed.RetentionPolicies).DataClass);
            using (var retry = await TenantPurgeFence.BeginPurgeAsync(tenantId, CancellationToken.None))
            {
                Assert.True(TenantPurgeFence.IsPurging(tenantId));
                retry.AbortBeforeDurableState();
            }
            Assert.False(TenantPurgeFence.IsPurging(tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Cancellation_during_upload_drain_releases_write_admission_before_tombstone()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        try
        {
            await using var db = CreateDb();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "cancel-drain", CompanyName = "Cancel Drain Co" });
            await db.SaveChangesAsync();
            var uploads = new TenantObjectUploadTracker();
            using var pendingUpload = uploads.Track(tenantId);
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, uploads: uploads);
            using var cancellation = new CancellationTokenSource();

            var purgeTask = purge.PurgeAsync(tenantId, cancellation.Token);
            await WaitUntilAsync(() => Task.FromResult(TenantPurgeFence.IsPurging(tenantId)));
            Assert.False(purgeTask.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purgeTask);

            Assert.Equal("Active", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            await AssertWriteAdmissionReleasedAsync(tenantId);
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Direct_purge_cancellation_before_lifecycle_commit_releases_write_admission()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        try
        {
            await using var db = CreateDb();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "cancel-lifecycle", CompanyName = "Cancel Lifecycle Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            using var cancellation = new CancellationTokenSource();
            lifecycleStore.BeforeSave = state =>
            {
                if (!state.IsPurging) return Task.CompletedTask;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purge.PurgeAsync(tenantId, cancellation.Token));

            lifecycleStore.BeforeSave = null;
            db.ChangeTracker.Clear();
            Assert.Null(await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None));
            Assert.Equal("Active", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            await AssertWriteAdmissionReleasedAsync(tenantId);

            await purge.PurgeAsync(tenantId, CancellationToken.None);
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Cancellation_after_lifecycle_commit_keeps_write_admission_closed()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        try
        {
            await using var db = CreateDb();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "cancel-after-commit", CompanyName = "Cancel After Commit Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            using var cancellation = new CancellationTokenSource();
            lifecycleStore.AfterSave = state =>
            {
                if (!state.IsPurging) return Task.CompletedTask;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purge.PurgeAsync(tenantId, cancellation.Token));

            db.ChangeTracker.Clear();
            var persisted = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.True(persisted.IsPurging);
            Assert.Equal("Active", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            await AssertWriteAdmissionBlockedAsync(tenantId);
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Cancellation_after_relational_commit_keeps_write_admission_closed()
    {
        var root = NewRoot();
        var tenantId = Guid.NewGuid();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var interceptor = new CancelAfterCommitInterceptor(cancellation);
            var options = new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .AddInterceptors(interceptor)
                .Options;
            await using var db = new RegentDbContext(options);
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "cancel-relational-commit", CompanyName = "Cancel Relational Commit Co" });
            await db.SaveChangesAsync();
            interceptor.Enabled = true;
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purge.PurgeAsync(tenantId, cancellation.Token));

            db.ChangeTracker.Clear();
            Assert.Equal("Purging", (await db.Tenants.SingleAsync(item => item.Id == tenantId)).Status);
            await AssertWriteAdmissionBlockedAsync(tenantId);
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            Delete(root);
        }
    }

    [Fact]
    public async Task Proof_finalization_failure_keeps_purging_tombstone_until_retry()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "proof", CompanyName = "Proof Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new TestLifecycleStore(new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json")));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));
            lifecycleStore.BeforeSave = state =>
            {
                if (state.DeletionProofs.Count > 0) throw new InvalidOperationException("Proof finalization failed.");
                return Task.CompletedTask;
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            var pending = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(pending!.IsPurging);
            Assert.Empty(pending.DeletionProofs);

            lifecycleStore.BeforeSave = null;
            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
            Assert.Equal(proof.Id, Assert.Single((await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None))!.DeletionProofs).Id);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Invalid_tenant_object_key_fails_before_relational_delete_and_is_retryable()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "invalid-key", CompanyName = "Invalid Key Co" });
            await db.SaveChangesAsync();
            var clock = new MutableTimeProvider();
            var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "lifecycle.json"));
            var objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var artifact = new ArtifactRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(), ObjectKey = "other-tenant/artifact",
                ContentType = "text/plain", CreatedAt = clock.GetUtcNow()
            };
            db.Artifacts.Add(artifact);
            await db.SaveChangesAsync();
            var purge = new TenantPurgeService(db, objects, [], vectors, exports, lifecycleStore, clock);
            var service = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
            var request = await service.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                CancellationToken.None);
            clock.Advance(TimeSpan.FromDays(8));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));
            Assert.True(await db.Tenants.AnyAsync(item => item.Id == tenantId, CancellationToken.None));
            var pending = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.True(pending!.IsPurging);
            Assert.Empty(pending.DeletionProofs);

            artifact.ObjectKey = $"tenants/{tenantId}/artifacts/{artifact.Id}";
            await db.SaveChangesAsync();
            var proof = await service.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
            Assert.Equal(proof.Id, Assert.Single((await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None))!.DeletionProofs).Id);
        }
        finally { Delete(root); }
    }

    private static RegentDbContext CreateDb(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<RegentDbContext>();
        if (connectionString is null) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        else options.UseSqlite(connectionString);
        return new RegentDbContext(options.Options);
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"purge-adversarial-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition was not met.");
    }

    private static async Task AssertWriteAdmissionReleasedAsync(Guid tenantId)
    {
        Assert.False(TenantPurgeFence.IsPurging(tenantId));
        using var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
        Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));
    }

    private static async Task AssertWriteAdmissionBlockedAsync(Guid tenantId)
    {
        Assert.True(TenantPurgeFence.IsPurging(tenantId));
        await Assert.ThrowsAsync<TenantPurgeInProgressException>(async () =>
            await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None));
    }

    private sealed class CancelAfterCommitInterceptor(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Enabled) return base.SavedChangesAsync(eventData, result, cancellationToken);
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        }
    }

    private sealed class TestLifecycleStore(IDocumentStore<DataLifecycleState> inner) : IDocumentStore<DataLifecycleState>
    {
        public Func<DataLifecycleState, Task>? BeforeSave { get; set; }
        public Func<DataLifecycleState, Task>? AfterSave { get; set; }
        public TaskCompletionSource PurgingSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPurge { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<DataLifecycleState>> GetAllAsync(CancellationToken cancellationToken) =>
            inner.GetAllAsync(cancellationToken);
        public Task<DataLifecycleState?> GetAsync(string id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);
        public Task<DataLifecycleState> GetOrAddAsync(string id, Func<DataLifecycleState> valueFactory,
            CancellationToken cancellationToken) => inner.GetOrAddAsync(id, valueFactory, cancellationToken);
        public async Task<DataLifecycleState> MutateAsync(string id, Func<DataLifecycleState?, DataLifecycleState> mutation,
            CancellationToken cancellationToken)
        {
            var result = await inner.MutateAsync(id, current =>
            {
                var next = mutation(current);
                if (BeforeSave is not null) BeforeSave(next).GetAwaiter().GetResult();
                return next;
            }, cancellationToken);
            if (AfterSave is not null) await AfterSave(result);
            return result;
        }
        public async Task<DataLifecycleState> MutateAsync(string id, Func<DataLifecycleState?, Task<DataLifecycleState>> mutation,
            CancellationToken cancellationToken)
        {
            var result = await inner.MutateAsync(id, async current =>
            {
                var next = await mutation(current);
                if (BeforeSave is not null) await BeforeSave(next);
                return next;
            }, cancellationToken);
            if (AfterSave is not null) await AfterSave(result);
            return result;
        }
        public Task PutAsync(string id, DataLifecycleState value, CancellationToken cancellationToken) =>
            inner.PutAsync(id, value, cancellationToken);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
        public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            inner.DeleteTenantAsync(tenantId, cancellationToken);
    }

    private sealed class FaultingObjectStore(IObjectStore inner) : IObjectStore
    {
        public bool FailDeletePrefix { get; set; }
        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
            FailDeletePrefix ? throw new InvalidOperationException("Object deletion failed.") : inner.DeletePrefixAsync(prefix, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class FaultingTenantJsonStore : ITenantJsonDocumentStore
    {
        public bool FailDeleteTenant { get; set; }
        public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            FailDeleteTenant ? throw new InvalidOperationException("JSON deletion failed.") : Task.FromResult(0);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class BlockingContextStore : IDocumentStore<AgentContextSnapshot>, ITenantJsonDocumentStore
    {
        private readonly Dictionary<string, AgentContextSnapshot> values = new(StringComparer.Ordinal);
        private int putCalls;
        public TaskCompletionSource PutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PutCalls => Volatile.Read(ref putCalls);

        public async Task PutAsync(string id, AgentContextSnapshot value, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref putCalls) == 1)
            {
                PutStarted.TrySetResult();
                await AllowPut.Task.WaitAsync(cancellationToken);
            }
            values[id] = value;
        }

        public Task<IReadOnlyList<AgentContextSnapshot>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentContextSnapshot>>(values.Values.ToArray());
        public Task<AgentContextSnapshot?> GetAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(values.GetValueOrDefault(id));
        public Task<AgentContextSnapshot> GetOrAddAsync(string id, Func<AgentContextSnapshot> valueFactory,
            CancellationToken cancellationToken) => Task.FromResult(values.TryGetValue(id, out var value) ? value : values[id] = valueFactory());
        public Task<AgentContextSnapshot> MutateAsync(string id, Func<AgentContextSnapshot?, AgentContextSnapshot> mutation,
            CancellationToken cancellationToken) => Task.FromResult(values[id] = mutation(values.GetValueOrDefault(id)));
        public Task<AgentContextSnapshot> MutateAsync(string id, Func<AgentContextSnapshot?, Task<AgentContextSnapshot>> mutation,
            CancellationToken cancellationToken) => MutateAsync(id, current => mutation(current).GetAwaiter().GetResult(), cancellationToken);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => Task.FromResult(values.Remove(id));
        public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var keys = values.Where(item => item.Key.StartsWith($"{tenantId}:", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Key).ToArray();
            foreach (var key in keys) values.Remove(key);
            return Task.FromResult(keys.Length);
        }
    }

    private sealed class BlockingPutObjectStore(IObjectStore inner) : IObjectStore
    {
        private int putCalls;
        public TaskCompletionSource PutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref putCalls) != 1) return await inner.PutAsync(request, cancellationToken);
            PutStarted.TrySetResult();
            await AllowPut.Task.WaitAsync(cancellationToken);
            return await inner.PutAsync(request, cancellationToken);
        }

        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
            inner.DeletePrefixAsync(prefix, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }
}
