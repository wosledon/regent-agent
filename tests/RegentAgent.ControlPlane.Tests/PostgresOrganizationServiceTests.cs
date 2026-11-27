using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PostgresOrganizationServiceTests
{
    [PostgresFact]
    public async Task Postgres_organization_service_maintains_ltree_paths_and_depth()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([
                    new Claim("tenant_id", tenantId.ToString("D"))
                ], "test"))
            }
        };
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(new TenantRlsConnectionInterceptor(accessor)).Options);
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        try
        {
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "synthetic-pg-org", CompanyName = "Synthetic PG Org" });
            await db.SaveChangesAsync();
            await service.StartCompanyAsync(CancellationToken.None);
            var root = Assert.Single(await service.ListPositionsAsync(CancellationToken.None));
            var source = await service.CreatePositionAsync(Position("Source", root.Id), CancellationToken.None);
            var child = await service.CreatePositionAsync(Position("Child", source.Id), CancellationToken.None);
            var grandchild = await service.CreatePositionAsync(Position("Grandchild", child.Id), CancellationToken.None);
            var sibling = await service.SplitPositionAsync(grandchild.Id, "Sibling", "Synthetic", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreatePositionAsync(Position("Too deep", grandchild.Id), CancellationToken.None));
            var beforeMerge = await db.Positions.ToDictionaryAsync(position => position.Id);
            Assert.Equal(beforeMerge[grandchild.Id].ParentPositionId, beforeMerge[sibling.Id].ParentPositionId);
            Assert.Equal(PositionPath.Sibling(beforeMerge[grandchild.Id].Path, grandchild.Id, sibling.Id),
                beforeMerge[sibling.Id].Path);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                service.MergePositionsAsync(source.Id, Guid.NewGuid(), CancellationToken.None));

            await service.MergePositionsAsync(source.Id, root.Id, CancellationToken.None);

            var positions = await db.Positions.ToDictionaryAsync(position => position.Id);
            Assert.Equal(root.Id, positions[child.Id].ParentPositionId);
            Assert.Equal($"{PositionPath.Root(root.Id)}.{child.Id:N}", positions[child.Id].Path);
            Assert.Equal($"{PositionPath.Root(root.Id)}.{child.Id:N}.{grandchild.Id:N}", positions[grandchild.Id].Path);
            Assert.All(positions.Values, position => Assert.True(position.Path.StartsWith($"root.{position.Id:N}", StringComparison.Ordinal)
                || position.Path.Contains($".{position.Id:N}", StringComparison.Ordinal)));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, true);";
                command.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
                await command.ExecuteNonQueryAsync();
                command.CommandText = """
                    DELETE FROM "EmploymentPeriods" WHERE "TenantId" = @tenant_id::uuid;
                    DELETE FROM "Employees" WHERE "TenantId" = @tenant_id::uuid;
                    DELETE FROM "Positions" WHERE "TenantId" = @tenant_id::uuid;
                    DELETE FROM "Tenants" WHERE "Id" = @tenant_id::uuid;
                    """;
                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
    }

    private static PositionDto Position(string name, Guid? parentId) =>
        new(Guid.Empty, name, "Synthetic responsibility", [], 0, parentId, "Open", 1);

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(Guid.NewGuid());
        public string DisplayName => "Synthetic Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class TestQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
