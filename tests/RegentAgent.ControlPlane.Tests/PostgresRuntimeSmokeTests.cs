using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Organization;
using RegentAgent.Memory;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("Postgres runtime", DisableParallelization = true)]
public sealed class PostgresRuntimeCollection;

[Collection("Postgres runtime")]
public sealed class PostgresRuntimeSmokeTests
{
    [PostgresFact]
    public async Task Controlplane_postgres_runtime_captures_searches_purges_and_maintains_paths()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        await PostgresTestSupport.AssertRuntimePrivilegesAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var factory = new ControlPlaneRuntimeFactory("Production", "Postgres", false, connectionString);
        using var client = factory.CreateClient();
        Assert.IsType<PostgresMemoryVectorStore>(factory.Services.GetRequiredService<IMemoryVectorStore>());
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            SetTenantContext(factory.Services.GetRequiredService<IHttpContextAccessor>(), tenantId);
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "synthetic-runtime", CompanyName = "Synthetic Runtime" });
            db.Users.Add(new UserRecord { Id = userId, Email = $"synthetic-{userId:N}@example.test", DisplayName = "Synthetic Owner" });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = "Owner" });
            await db.SaveChangesAsync();

            var memory = scope.ServiceProvider.GetRequiredService<MemoryService>();
            var index = scope.ServiceProvider.GetRequiredService<MemoryIndexService>();
            var bracedTenant = $"{{{tenantId.ToString("D").ToUpperInvariant()}}}";
            var compactTenant = tenantId.ToString("N").ToUpperInvariant();
            var organization = await memory.CaptureAsync(bracedTenant, "organization",
                "Synthetic launch checkpoint", "source:synthetic", 0.9, "trusted", "Synthetic Owner", CancellationToken.None);
            var alice = await memory.CaptureAsync(compactTenant, "personal",
                "Synthetic Alice launch note", "source:synthetic", 0.9, "trusted", "Alice", CancellationToken.None,
                authorUserId: "user-alice");
            var bob = await memory.CaptureAsync(tenantId.ToString("D"), "personal",
                "Synthetic Bob launch note", "source:synthetic", 0.9, "trusted", "Bob", CancellationToken.None,
                authorUserId: "user-bob");
            var foreign = await memory.CaptureAsync(otherTenantId.ToString("D"), "organization",
                "Synthetic foreign launch checkpoint", "source:synthetic", 0.9, "trusted", "Synthetic Owner", CancellationToken.None);
            Assert.Equal(3, await index.RebuildAsync(compactTenant, CancellationToken.None));
            await index.IndexClaimAsync(organization with { TenantId = tenantId.ToString("D") }, CancellationToken.None);

            var aliceHits = await index.SearchHybridAsync(bracedTenant, "Synthetic launch", null, 10, "Alice",
                CancellationToken.None, viewerUserId: "user-alice");
            var bobHits = await index.SearchHybridAsync(compactTenant, "Synthetic launch", null, 10, "Bob",
                CancellationToken.None, viewerUserId: "user-bob");
            var foreignHits = await index.SearchHybridAsync(otherTenantId.ToString("D"), "Synthetic launch", null, 10, "Synthetic Owner", CancellationToken.None);
            Assert.Contains(aliceHits.Hits, hit => hit.ClaimId == organization.Id);
            Assert.Contains(aliceHits.Hits, hit => hit.ClaimId == alice.Id);
            Assert.DoesNotContain(aliceHits.Hits, hit => hit.ClaimId == bob.Id || hit.ClaimId == foreign.Id);
            Assert.Contains(bobHits.Hits, hit => hit.ClaimId == bob.Id);
            Assert.DoesNotContain(bobHits.Hits, hit => hit.ClaimId == alice.Id || hit.ClaimId == foreign.Id);
            Assert.Single(foreignHits.Hits);
            Assert.Equal(foreign.Id, foreignHits.Hits[0].ClaimId);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                memory.DeleteAsync(alice.Id, otherTenantId.ToString("D"), CancellationToken.None));

            var vectors = scope.ServiceProvider.GetRequiredService<IMemoryVectorStore>();
            Assert.Equal(3, (await vectors.GetAllAsync(tenantId.ToString("D"), CancellationToken.None)).Count);
            Assert.Single(await vectors.GetAllAsync(otherTenantId.ToString("D"), CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(factory.Root, "data", "modules", "memory-vectors.json")));

            var organizationService = new OrganizationService(db, new RuntimeTestTenantContext(tenantId), TimeProvider.System, new RuntimeTestQuotaGuard());
            await organizationService.StartCompanyAsync(CancellationToken.None);
            var root = Assert.Single(await organizationService.ListPositionsAsync(CancellationToken.None));
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

            await scope.ServiceProvider.GetRequiredService<TenantPurgeService>().PurgeAsync(tenantId, CancellationToken.None);
            Assert.Empty(await vectors.GetAllAsync(tenantId.ToString("D"), CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(otherTenantId.ToString("D"), CancellationToken.None));
            await vectors.DeleteTenantAsync(otherTenantId.ToString("D"), CancellationToken.None);
        }
        finally
        {
            await CleanupAsync(connectionString, userId, tenantId, otherTenantId);
        }
    }

    private static PositionDto Position(string name, Guid? parentId) =>
        new(Guid.Empty, name, "Synthetic responsibility", [], 0, parentId, "Open", 1);

    private static void SetTenantContext(IHttpContextAccessor accessor, Guid tenantId)
    {
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString("D"))], "test"))
        };
    }

    private static async Task CleanupAsync(string connectionString, Guid userId, params Guid[] tenantIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var tenantId in tenantIds)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT set_config('app.tenant_id', @tenant_id::text, true);
                DELETE FROM "EmploymentPeriods" WHERE "TenantId" = @tenant_id;
                DELETE FROM "Employees" WHERE "TenantId" = @tenant_id;
                DELETE FROM "Positions" WHERE "TenantId" = @tenant_id;
                DELETE FROM "Memberships" WHERE "TenantId" = @tenant_id;
                DELETE FROM "Tenants" WHERE "Id" = @tenant_id;
                DELETE FROM "MemoryEmbeddings" WHERE "TenantId" = @tenant_id;
                """;
            command.Parameters.AddWithValue("tenant_id", tenantId);
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM \"Users\" WHERE \"Id\" = @user_id;";
            command.Parameters.AddWithValue("user_id", userId);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
