using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Governance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TenantPurgeFenceTests
{
    [Fact]
    public async Task Direct_write_admission_is_released_on_dispose()
    {
        var tenantId = Guid.NewGuid();
        var admission = await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
        Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));

        admission.Dispose();

        Assert.Equal(0, TenantPurgeFence.GetActiveWrites(tenantId));
        TenantPurgeFence.Reset(tenantId);
    }

    [Fact]
    public async Task Cancellation_while_draining_releases_in_memory_purge_latch()
    {
        var tenantId = Guid.NewGuid();
        async Task<IDisposable> EnterWriteAsync() =>
            await TenantPurgeFence.EnterWriteAsync(tenantId, CancellationToken.None);
        try
        {
            var write = await EnterWriteAsync();
            using var cancellation = new CancellationTokenSource();
            var purge = TenantPurgeFence.BeginPurgeAsync(tenantId, cancellation.Token);
            Assert.True(TenantPurgeFence.IsPurging(tenantId));
            Assert.False(purge.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purge);
            write.Dispose();

            Assert.False(TenantPurgeFence.IsPurging(tenantId));
            using (var resumedWrite = await EnterWriteAsync())
                Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenantId));
            using var resumedPurge = await TenantPurgeFence.BeginPurgeAsync(tenantId, CancellationToken.None);
            Assert.True(TenantPurgeFence.IsPurging(tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
        }
    }

    [Fact]
    public async Task Json_store_releases_write_admission_after_put()
    {
        var tenantId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"purge-fence-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDocumentStore<DataLifecycleState>(path);
            await store.PutAsync(tenantId.ToString(), new DataLifecycleState { TenantId = tenantId }, CancellationToken.None);

            Assert.Equal(0, TenantPurgeFence.GetActiveWrites(tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Json_store_checks_durable_state_and_releases_admission_when_active()
    {
        var tenantId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"purge-fence-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDocumentStore<DataLifecycleState>(path, new StubPurgeStateProvider(false));
            await store.PutAsync(tenantId.ToString(), new DataLifecycleState { TenantId = tenantId }, CancellationToken.None);

            Assert.Equal(0, TenantPurgeFence.GetActiveWrites(tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Json_store_fails_closed_when_durable_state_blocks_writes()
    {
        var tenantId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"purge-fence-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDocumentStore<DataLifecycleState>(path, new StubPurgeStateProvider(true));

            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() => store.PutAsync(tenantId.ToString(),
                new DataLifecycleState { TenantId = tenantId }, CancellationToken.None));

            Assert.False(File.Exists(path));
            Assert.Equal(0, TenantPurgeFence.GetActiveWrites(tenantId));
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Write_interceptor_fails_closed_from_durable_state()
    {
        var tenantId = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), $"purge-interceptor-{Guid.NewGuid():N}.db");
        try
        {
            var purgeState = new StubPurgeStateProvider(false);
            var options = new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}")
                .AddInterceptors(new TenantPurgeWriteInterceptor(purgeState))
                .Options;
            await using var db = new RegentDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "blocked", CompanyName = "Blocked Co" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            purgeState.Blocked = true;
            db.Artifacts.Add(new ArtifactRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = Guid.NewGuid(),
                ObjectKey = $"tenants/{tenantId}/artifacts/blocked", ContentType = "text/plain",
                CreatedAt = DateTimeOffset.UtcNow
            });

            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() => db.SaveChangesAsync());
        }
        finally
        {
            TenantPurgeFence.Reset(tenantId);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class StubPurgeStateProvider(bool blocked) : ITenantPurgeStateProvider
    {
        public bool Blocked { get; set; } = blocked;
        public Task<bool> IsPurgeActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult(Blocked);
    }
}
