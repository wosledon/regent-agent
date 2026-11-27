using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

[Collection("Postgres runtime")]
public sealed class PostgresPurgeEligibilityRaceTests
{
    [PostgresFact]
    public async Task Project_purge_preserves_tenant_and_relational_canary()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var projectId = Guid.NewGuid();
        var seedDb = fixture.CreateDbContext(tenantId);
        seedDb.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = "canary", Mission = "Synthetic",
            Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
            TargetAt = fixture.Clock.GetUtcNow(), UpdatedAt = fixture.Clock.GetUtcNow()
        });
        await seedDb.SaveChangesAsync(CancellationToken.None);
        var request = await fixture.CreateLifecycleService(tenantId)
            .SoftDeleteAsync(tenantId, "Project", projectId.ToString(), "Project removed", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(31));

        var proof = await fixture.CreateLifecycleService(tenantId)
            .PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);

        Assert.Equal("Project", proof.ResourceType);
        Assert.Equal(projectId.ToString(), proof.ResourceId);
        var state = await fixture.LifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
        Assert.NotNull(state);
        Assert.False(state.IsPurged);
        Assert.Equal(proof.Id, Assert.Single(state.DeletionProofs).Id);
        Assert.DoesNotContain(state.DeletionProofs, item =>
            item.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase));
        var db = fixture.CreateDbContext(tenantId);
        db.ChangeTracker.Clear();
        Assert.True(await db.Tenants.AnyAsync(item => item.Id == tenantId));
        Assert.True(await db.Projects.AnyAsync(item => item.Id == projectId));
        Assert.False(TenantPurgeFence.IsPurging(tenantId));
        using var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
        Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));
    }

    [PostgresFact]
    public async Task Legal_hold_committed_before_fenced_mutation_aborts_purge()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var request = await fixture.CreateLifecycleService(tenantId)
            .SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        var purgeStore = new SnapshotGatedStore(fixture.LifecycleStore);
        var purgeService = fixture.CreateLifecycleService(tenantId, purgeStore);

        var purgeTask = purgeService.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
        await purgeStore.SnapshotRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.CreateLifecycleService(tenantId).CreateLegalHoldAsync(tenantId, new LegalHoldRecord
        {
            ResourceRef = tenantId.ToString(), Reason = "Investigation", AuthorizedBy = "Operator"
        }, CancellationToken.None);
        purgeStore.AllowSnapshot.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => purgeTask);
        await fixture.AssertPurgeNotStartedAsync(tenantId, request.Id);
    }

    [PostgresFact]
    public async Task Restore_committed_before_fenced_mutation_aborts_purge()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var lifecycle = fixture.CreateLifecycleService(tenantId);
        var request = await lifecycle.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
            CancellationToken.None);
        var purgeStore = new SnapshotGatedStore(fixture.LifecycleStore);
        var purgeService = fixture.CreateLifecycleService(tenantId, purgeStore);

        var purgeTask = purgeService.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
        await purgeStore.SnapshotRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await lifecycle.RestoreAsync(tenantId, request.Id, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        purgeStore.AllowSnapshot.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => purgeTask);
        await fixture.AssertPurgeNotStartedAsync(tenantId, request.Id, expectedStatus: "Restored");
    }

    [PostgresFact]
    public async Task Lifecycle_object_key_committed_before_fenced_mutation_aborts_purge()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var lifecycle = fixture.CreateLifecycleService(tenantId);
        await lifecycle.ArchiveAsync(tenantId, "2026-11", "Archive", 1, CancellationToken.None);
        var request = await lifecycle.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        var purgeStore = new SnapshotGatedStore(fixture.LifecycleStore);
        var purgeService = fixture.CreateLifecycleService(tenantId, purgeStore);

        var purgeTask = purgeService.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);
        await purgeStore.SnapshotRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.LifecycleStore.MutateAsync(tenantId.ToString(), state =>
        {
            state!.Archives.Single().ObjectKey = "other-tenant/archive.json";
            return state;
        }, CancellationToken.None);
        purgeStore.AllowSnapshot.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => purgeTask);
        await fixture.AssertPurgeNotStartedAsync(tenantId, request.Id);
    }

    [PostgresFact]
    public async Task Pending_upload_drains_before_purge_and_late_upload_is_rejected()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var uploads = new TenantObjectUploadTracker();
        var inner = new BlockingPutObjectStore(new LocalFileObjectStore(fixture.ObjectRoot));
        var objects = new TenantPurgeObjectStore(inner, uploads, fixture.PurgeState);
        var key = $"tenants/{tenantId}/artifacts/pending";
        await using var content = new MemoryStream("pending"u8.ToArray());

        var uploadTask = objects.PutAsync(new ObjectUploadRequest(key, content, "text/plain"), CancellationToken.None);
        await inner.PutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var purge = new TenantPurgeService(fixture.CreateDbContext(tenantId), objects, [], fixture.Vectors,
            fixture.Exports, fixture.LifecycleStore, fixture.Clock, uploads);
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

    [PostgresFact]
    public async Task Cancellation_before_lifecycle_commit_releases_write_admission()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var request = await fixture.CreateLifecycleService(tenantId)
            .SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        using var cancellation = new CancellationTokenSource();
        var store = new TestLifecycleStore(fixture.LifecycleStore);
        store.BeforeSave = state =>
        {
            if (!state.IsPurging) return Task.CompletedTask;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        var lifecycle = fixture.CreateLifecycleService(tenantId, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.PurgeAsync(tenantId, request.Id, "PURGE", cancellation.Token));

        await fixture.AssertPurgeNotStartedAsync(tenantId, request.Id);
    }

    [PostgresFact]
    public async Task Cancellation_after_lifecycle_commit_keeps_write_admission_closed()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var request = await fixture.CreateLifecycleService(tenantId)
            .SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        using var cancellation = new CancellationTokenSource();
        var store = new TestLifecycleStore(fixture.LifecycleStore);
        store.AfterSave = state =>
        {
            if (!state.IsPurging) return Task.CompletedTask;
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };
        var lifecycle = fixture.CreateLifecycleService(tenantId, store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.PurgeAsync(tenantId, request.Id, "PURGE", cancellation.Token));

        var state = await fixture.LifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
        Assert.NotNull(state);
        Assert.True(state.IsPurging);
        Assert.Empty(state.DeletionProofs);
        Assert.Equal("SoftDeleted", (await fixture.CreateDbContext(tenantId).Tenants
            .SingleAsync(item => item.Id == tenantId)).Status);
        await fixture.AssertWriteAdmissionBlockedAsync(tenantId);
    }

    [PostgresFact]
    public async Task Cancellation_after_relational_commit_keeps_write_admission_closed()
    {
        await using var fixture = await PostgresPurgeFixture.CreateAsync();
        var tenantId = Guid.NewGuid();
        await fixture.SeedTenantAsync(tenantId);
        var lifecycle = fixture.CreateLifecycleService(tenantId);
        var request = await lifecycle.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromDays(8));
        fixture.RelationalCancellation.Enable();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            lifecycle.PurgeAsync(tenantId, request.Id, "PURGE", fixture.RelationalCancellation.Token));

        var db = fixture.CreateDbContext(tenantId);
        db.ChangeTracker.Clear();
        Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId));
        var state = await fixture.LifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
        Assert.NotNull(state);
        Assert.True(state.IsPurging);
        Assert.Empty(state.DeletionProofs);
        await fixture.AssertWriteAdmissionBlockedAsync(tenantId);
    }

    private sealed class PostgresPurgeFixture : IAsyncDisposable
    {
        private readonly List<AsyncServiceScope> scopes = [];
        private readonly List<Guid> tenantIds = [];
        private readonly string root;

        private PostgresPurgeFixture(string connectionString, string root, ServiceProvider services,
            ITenantPurgeStateProvider purgeState, TenantObjectUploadTracker uploads,
            CancelAfterRelationalCommitInterceptor relationalCancellation)
        {
            ConnectionString = connectionString;
            this.root = root;
            Services = services;
            PurgeState = purgeState;
            Uploads = uploads;
            RelationalCancellation = relationalCancellation;
            LifecycleStore = new JsonDocumentStore<DataLifecycleState>(
                Path.Combine(root, "data", "modules", "lifecycle.json"), purgeState);
            Exports = new JsonDocumentStore<DataExportDto>(
                Path.Combine(root, "data", "modules", "exports.json"), purgeState);
            Objects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            Vectors = new PostgresMemoryVectorStore(connectionString);
            Clock = new MutableTimeProvider();
        }

        public string ConnectionString { get; }
        public string ObjectRoot => Path.Combine(root, "objects");
        public ServiceProvider Services { get; }
        public ITenantPurgeStateProvider PurgeState { get; }
        public TenantObjectUploadTracker Uploads { get; }
        public CancelAfterRelationalCommitInterceptor RelationalCancellation { get; }
        public JsonDocumentStore<DataLifecycleState> LifecycleStore { get; }
        public JsonDocumentStore<DataExportDto> Exports { get; }
        public IObjectStore Objects { get; }
        public PostgresMemoryVectorStore Vectors { get; }
        public MutableTimeProvider Clock { get; }

        public static async Task<PostgresPurgeFixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
            await PostgresTestSupport.AssertSupportedVersionAsync(connectionString);

            var root = Path.Combine(Path.GetTempPath(), $"postgres-purge-race-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var lifecyclePath = Path.Combine(root, "data", "modules", "lifecycle.json");
            var accessor = new HttpContextAccessor();
            var services = new ServiceCollection();
            services.AddSingleton<IHttpContextAccessor>(accessor);
            services.AddSingleton<ITenantPurgeStateProvider>(provider => new PersistedTenantPurgeStateProvider(
                provider.GetRequiredService<IServiceScopeFactory>(), lifecyclePath));
            services.AddScoped<TenantPurgeWriteInterceptor>();
            services.AddSingleton<CancelAfterRelationalCommitInterceptor>();
            services.AddDbContext<RegentDbContext>((provider, options) => options.UseNpgsql(connectionString)
                .AddInterceptors(new TenantRlsConnectionInterceptor(accessor))
                .AddInterceptors(provider.GetRequiredService<TenantPurgeWriteInterceptor>())
                .AddInterceptors(provider.GetRequiredService<CancelAfterRelationalCommitInterceptor>()));
            var serviceProvider = services.BuildServiceProvider();
            return new PostgresPurgeFixture(connectionString, root, serviceProvider,
                serviceProvider.GetRequiredService<ITenantPurgeStateProvider>(), new TenantObjectUploadTracker(),
                serviceProvider.GetRequiredService<CancelAfterRelationalCommitInterceptor>());
        }

        public async Task SeedTenantAsync(Guid tenantId)
        {
            tenantIds.Add(tenantId);
            var db = CreateDbContext(tenantId);
            db.Tenants.Add(new TenantRecord
            {
                Id = tenantId, Name = $"purge-race-{tenantId:N}", CompanyName = "Purge Race Co"
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        public DataLifecycleService CreateLifecycleService(Guid tenantId,
            IDocumentStore<DataLifecycleState>? lifecycleStore = null, IObjectStore? objects = null,
            TenantObjectUploadTracker? uploads = null)
        {
            var db = CreateDbContext(tenantId);
            var store = lifecycleStore ?? LifecycleStore;
            var objectStore = objects ?? Objects;
            var uploadTracker = uploads ?? Uploads;
            var purge = new TenantPurgeService(db, objectStore, [], Vectors, Exports, store, Clock, uploadTracker);
            return new DataLifecycleService(store, objectStore, db, Clock, purge, new TenantRlsScope(db));
        }

        public RegentDbContext CreateDbContext(Guid tenantId)
        {
            var scope = Services.CreateAsyncScope();
            scopes.Add(scope);
            SetTenantContext(Services.GetRequiredService<IHttpContextAccessor>(), tenantId);
            return scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        }

        public async Task AssertPurgeNotStartedAsync(Guid tenantId, Guid requestId, string expectedStatus = "SoftDeleted")
        {
            var state = await LifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
            Assert.NotNull(state);
            Assert.Equal(expectedStatus, Assert.Single(state.DeletionRequests).Status);
            Assert.Empty(state.DeletionProofs);
            Assert.False(TenantPurgeFence.IsPurging(tenantId));
            using var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
            Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));
            Assert.True(await CreateDbContext(tenantId).Tenants.AnyAsync(item => item.Id == tenantId));
        }

        public async Task AssertWriteAdmissionBlockedAsync(Guid tenantId)
        {
            Assert.True(TenantPurgeFence.IsPurging(tenantId));
            await Assert.ThrowsAsync<TenantPurgeInProgressException>(async () =>
                await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var tenantId in tenantIds) TenantPurgeFence.Reset(tenantId);
            RelationalCancellation.Reset();
            foreach (var scope in scopes) await scope.DisposeAsync();
            await Vectors.DisposeAsync();
            await Services.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private static void SetTenantContext(IHttpContextAccessor accessor, Guid tenantId)
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString("D"))], "test"))
            };
        }
    }

    private sealed class SnapshotGatedStore(IDocumentStore<DataLifecycleState> inner) : IDocumentStore<DataLifecycleState>
    {
        private int gateNextGet = 1;
        public TaskCompletionSource SnapshotRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSnapshot { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DataLifecycleState?> GetAsync(string id, CancellationToken cancellationToken)
        {
            var result = await inner.GetAsync(id, cancellationToken);
            if (Interlocked.Exchange(ref gateNextGet, 0) == 1)
            {
                SnapshotRead.TrySetResult();
                await AllowSnapshot.Task.WaitAsync(cancellationToken);
            }
            return result;
        }

        public Task<IReadOnlyList<DataLifecycleState>> GetAllAsync(CancellationToken cancellationToken) =>
            inner.GetAllAsync(cancellationToken);
        public Task<DataLifecycleState> GetOrAddAsync(string id, Func<DataLifecycleState> valueFactory,
            CancellationToken cancellationToken) => inner.GetOrAddAsync(id, valueFactory, cancellationToken);
        public Task<DataLifecycleState> MutateAsync(string id, Func<DataLifecycleState?, DataLifecycleState> mutation,
            CancellationToken cancellationToken) => inner.MutateAsync(id, mutation, cancellationToken);
        public Task<DataLifecycleState> MutateAsync(string id, Func<DataLifecycleState?, Task<DataLifecycleState>> mutation,
            CancellationToken cancellationToken) => inner.MutateAsync(id, mutation, cancellationToken);
        public Task PutAsync(string id, DataLifecycleState value, CancellationToken cancellationToken) =>
            inner.PutAsync(id, value, cancellationToken);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);
        public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            inner.DeleteTenantAsync(tenantId, cancellationToken);
    }

    private sealed class TestLifecycleStore(IDocumentStore<DataLifecycleState> inner) : IDocumentStore<DataLifecycleState>
    {
        public Func<DataLifecycleState, Task>? BeforeSave { get; set; }
        public Func<DataLifecycleState, Task>? AfterSave { get; set; }

        public Task<IReadOnlyList<DataLifecycleState>> GetAllAsync(CancellationToken cancellationToken) =>
            inner.GetAllAsync(cancellationToken);
        public Task<DataLifecycleState?> GetAsync(string id, CancellationToken cancellationToken) =>
            inner.GetAsync(id, cancellationToken);
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
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken) =>
            inner.DeleteAsync(id, cancellationToken);
        public Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
            inner.DeleteTenantAsync(tenantId, cancellationToken);
    }

    private sealed class BlockingPutObjectStore(IObjectStore inner) : IObjectStore
    {
        private int putCalls;
        public TaskCompletionSource PutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref putCalls) == 1)
            {
                PutStarted.TrySetResult();
                await AllowPut.Task.WaitAsync(cancellationToken);
            }
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

    private sealed class CancelAfterRelationalCommitInterceptor : SaveChangesInterceptor
    {
        private readonly CancellationTokenSource cancellation = new();
        private bool enabled;
        private bool cancelAfterCommit;

        public CancellationToken Token => cancellation.Token;
        public void Enable() => enabled = true;
        public void Reset()
        {
            enabled = false;
            cancelAfterCommit = false;
            cancellation.Cancel();
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            cancelAfterCommit = enabled && eventData.Context?.ChangeTracker.Entries().Any(entry =>
                entry.Entity is TenantRecord && entry.State == EntityState.Deleted) == true;
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (cancelAfterCommit)
            {
                cancelAfterCommit = false;
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }

        public override void SaveChangesFailed(DbContextErrorEventData eventData)
        {
            cancelAfterCommit = false;
            base.SaveChangesFailed(eventData);
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

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
}
