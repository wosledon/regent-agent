using RegentAgent.ControlPlane.Application;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ComplianceServiceTests
{
    [Fact]
    public async Task Missing_profile_is_atomically_initialized_to_deterministic_baseline()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compliance-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDocumentStore<ComplianceProfile>(path);
            var service = new ComplianceService(store);
            var tenantId = Guid.NewGuid().ToString();

            var profiles = await Task.WhenAll(Enumerable.Range(0, 16)
                .Select(_ => service.GetAsync(tenantId, CancellationToken.None)));

            Assert.All(profiles, profile => AssertBaseline(profile, tenantId));
            var persisted = Assert.Single(await store.GetAllAsync(CancellationToken.None));
            AssertBaseline(persisted, tenantId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Update_initializes_missing_profile_and_preserves_tenant_and_version_scope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compliance-update-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDocumentStore<ComplianceProfile>(path);
            var service = new ComplianceService(store);
            var tenantId = Guid.NewGuid().ToString();
            var otherTenantId = Guid.NewGuid().ToString();

            var updated = await service.UpdateAsync(tenantId, new ComplianceProfile(
                otherTenantId, "strict", ["OpenAI"], ["global"], 365, "worm", true, false, 1, "Draft"),
                CancellationToken.None);

            Assert.Equal(tenantId, updated.TenantId);
            Assert.Equal(2, updated.Version);
            Assert.Equal("Active", updated.Status);
            AssertBaseline(await service.GetAsync(otherTenantId, CancellationToken.None), otherTenantId);
            Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None)).Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Concurrent_updates_have_one_winner_and_version_conflicts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compliance-concurrent-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ComplianceService(new JsonDocumentStore<ComplianceProfile>(path));
            var tenantId = Guid.NewGuid().ToString();

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
            {
                try
                {
                    var profile = await service.UpdateAsync(tenantId, new ComplianceProfile(
                        tenantId, "custom", ["OpenAI"], ["global"], 180 + index, "standard", false, false, 1, "Draft"),
                        CancellationToken.None);
                    return (Profile: (ComplianceProfile?)profile, Error: (VersionConflictException?)null);
                }
                catch (VersionConflictException error)
                {
                    return (Profile: (ComplianceProfile?)null, Error: (VersionConflictException?)error);
                }
            }));

            var winner = Assert.Single(results, result => result.Error is null).Profile;
            Assert.NotNull(winner);
            Assert.Equal(2, winner.Version);
            Assert.Equal(7, results.Count(result => result.Error is not null));
            Assert.Equal(2, (await service.GetAsync(tenantId, CancellationToken.None)).Version);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Two_store_instances_serialize_updates_and_purge_without_lost_updates_or_recreation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compliance-shared-store-{Guid.NewGuid():N}.json");
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondStarted = NewSignal();
        var secondEntered = NewSignal();
        var releaseSecond = NewSignal();
        var updateEntered = NewSignal();
        var releaseUpdate = NewSignal();
        var purgeStarted = NewSignal();
        try
        {
            var first = new JsonDocumentStore<ComplianceProfile>(path);
            var second = new JsonDocumentStore<ComplianceProfile>(path);
            var tenantId = Guid.NewGuid();
            var key = tenantId.ToString();
            var baseline = new ComplianceProfile(key, "baseline", ["OpenAI", "Anthropic"], ["global"],
                180, "standard", false, false, 1, "Active");
            await first.PutAsync(key, baseline, CancellationToken.None);

            var firstUpdate = Task.Run(() => first.MutateAsync(key, current =>
            {
                firstEntered.SetResult();
                releaseFirst.Task.GetAwaiter().GetResult();
                return current! with { OnlineRetentionDays = current!.OnlineRetentionDays + 1 };
            }, CancellationToken.None));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var secondUpdate = Task.Run(() =>
            {
                secondStarted.SetResult();
                return second.MutateAsync(key, current =>
                {
                    secondEntered.SetResult();
                    releaseSecond.Task.GetAwaiter().GetResult();
                    return current! with { OnlineRetentionDays = current!.OnlineRetentionDays + 1 };
                }, CancellationToken.None);
            });
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotSame(secondEntered.Task, await Task.WhenAny(secondEntered.Task, Task.Delay(100)));
            releaseFirst.SetResult();
            await firstUpdate;
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseSecond.SetResult();
            await secondUpdate;
            Assert.Equal(182, (await first.GetAsync(key, CancellationToken.None))!.OnlineRetentionDays);

            await first.PutAsync(key, baseline, CancellationToken.None);
            var update = Task.Run(() => first.MutateAsync(key, current =>
            {
                updateEntered.SetResult();
                releaseUpdate.Task.GetAwaiter().GetResult();
                return current! with { OnlineRetentionDays = 365 };
            }, CancellationToken.None));
            await updateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var purge = Task.Run(() =>
            {
                purgeStarted.SetResult();
                return second.DeleteTenantAsync(tenantId, CancellationToken.None);
            });
            await purgeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.NotSame(purge, await Task.WhenAny(purge, Task.Delay(100)));
            releaseUpdate.SetResult();
            await update;
            await purge;
            Assert.Null(await first.GetAsync(key, CancellationToken.None));
        }
        finally
        {
            releaseFirst.TrySetResult();
            releaseSecond.TrySetResult();
            releaseUpdate.TrySetResult();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertBaseline(ComplianceProfile? profile, string tenantId)
    {
        Assert.NotNull(profile);
        Assert.Equal(tenantId, profile.TenantId);
        Assert.Equal("baseline", profile.Preset);
        Assert.Equal(["OpenAI", "Anthropic"], profile.AllowedProviders);
        Assert.Equal(["global"], profile.AllowedRegions);
        Assert.Equal(180, profile.OnlineRetentionDays);
        Assert.Equal("standard", profile.AuditProfile);
        Assert.False(profile.BackupRequired);
        Assert.False(profile.ExternalSharingAllowed);
        Assert.Equal(1, profile.Version);
        Assert.Equal("Active", profile.Status);
    }
}
