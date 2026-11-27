using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Organization;
using RegentAgent.Memory;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("Local runtime", DisableParallelization = true)]
public sealed class LocalRuntimeCollection;

[Collection("Local runtime")]
public sealed class LocalStorageRuntimeSmokeTests
{
    [Theory]
    [InlineData("InMemory", true)]
    [InlineData("Sqlite", false)]
    [InlineData("Postgres", true)]
    public async Task Local_runtime_uses_json_vectors_and_loop_hierarchy(string provider, bool useInMemory)
    {
        await using var factory = new ControlPlaneRuntimeFactory("Development", provider, useInMemory);
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.IsType<JsonMemoryVectorStore>(scope.ServiceProvider.GetRequiredService<IMemoryVectorStore>());
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "synthetic-local", CompanyName = "Synthetic Local" });
        await db.SaveChangesAsync();

        var memory = scope.ServiceProvider.GetRequiredService<MemoryService>();
        var index = scope.ServiceProvider.GetRequiredService<MemoryIndexService>();
        var organization = await memory.CaptureAsync(tenantId.ToString("D"), "organization",
            "Synthetic local launch checkpoint", "source:synthetic", 0.9, "trusted", "Owner", CancellationToken.None);
        var personal = await memory.CaptureAsync(tenantId.ToString("D"), "personal",
            "Synthetic private launch note", "source:synthetic", 0.9, "trusted", "Alice", CancellationToken.None,
            authorUserId: "user-alice");
        var foreign = await memory.CaptureAsync(Guid.NewGuid().ToString("D"), "organization",
            "Synthetic foreign launch checkpoint", "source:synthetic", 0.9, "trusted", "Owner", CancellationToken.None);
        var aliceHits = await index.SearchHybridAsync(tenantId.ToString("D"), "Synthetic launch", null, 10, "Alice",
            CancellationToken.None, viewerUserId: "user-alice");
        var bobHits = await index.SearchHybridAsync(tenantId.ToString("D"), "Synthetic launch", null, 10, "Bob",
            CancellationToken.None, viewerUserId: "user-bob");
        Assert.Contains(aliceHits.Hits, hit => hit.ClaimId == organization.Id);
        Assert.Contains(aliceHits.Hits, hit => hit.ClaimId == personal.Id);
        Assert.DoesNotContain(bobHits.Hits, hit => hit.ClaimId == personal.Id || hit.ClaimId == foreign.Id);
        Assert.True(File.Exists(Path.Combine(factory.Root, "data", "modules", "memory-vectors.json")));

        var organizationService = new OrganizationService(db, new RuntimeTestTenantContext(tenantId), TimeProvider.System, new RuntimeTestQuotaGuard());
        await organizationService.StartCompanyAsync(CancellationToken.None);
        var root = (await organizationService.ListPositionsAsync(CancellationToken.None)).Single();
        var source = await organizationService.CreatePositionAsync(Position("Source", root.Id), CancellationToken.None);
        var child = await organizationService.CreatePositionAsync(Position("Child", source.Id), CancellationToken.None);
        var grandchild = await organizationService.CreatePositionAsync(Position("Grandchild", child.Id), CancellationToken.None);
        var sibling = await organizationService.SplitPositionAsync(grandchild.Id, "Sibling", "Synthetic", CancellationToken.None);
        await organizationService.MergePositionsAsync(source.Id, root.Id, CancellationToken.None);
        var positions = await db.Positions.ToDictionaryAsync(position => position.Id);
        Assert.Equal(PositionPath.Root(root.Id), positions[root.Id].Path);
        Assert.Equal($"{PositionPath.Root(root.Id)}.{child.Id:N}", positions[child.Id].Path);
        Assert.Equal($"{PositionPath.Root(root.Id)}.{child.Id:N}.{grandchild.Id:N}", positions[grandchild.Id].Path);
        Assert.Equal(PositionPath.Sibling(positions[grandchild.Id].Path, grandchild.Id, sibling.Id), positions[sibling.Id].Path);

        await memory.DeleteAsync(personal.Id, tenantId.ToString("D"), CancellationToken.None);
        Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<IMemoryVectorStore>()
            .GetAllAsync(tenantId.ToString("D"), CancellationToken.None), vector => vector.ClaimId == personal.Id);
    }

    private static PositionDto Position(string name, Guid? parentId) =>
        new(Guid.Empty, name, "Synthetic responsibility", [], 0, parentId, "Open", 1);
}
