using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class LocalSchemaUpgradeTests
{
    [Fact]
    public async Task Sqlite_upgrade_adds_and_backfills_milestone_tenant_scope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"regent-schema-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Milestones\";");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "Milestones" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "ProjectId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "TargetAt" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "AcceptanceRule" TEXT NOT NULL
                );
                """);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var milestoneId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Projects"
                    ("Id", "TenantId", "Name", "Mission", "Owner", "Stage", "Health", "TargetAt", "BudgetMinor", "SpentMinor", "Currency", "UpdatedAt")
                VALUES
                    ({projectId}, {tenantId}, 'Project', 'Mission', 'Owner', 'Active', 'Healthy', '2026-01-01T00:00:00+00:00', 0, 0, 'CNY', '2026-01-01T00:00:00+00:00');
                INSERT INTO "Milestones" ("Id", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
                VALUES ({milestoneId}, {projectId}, 'Milestone', '2026-01-02T00:00:00+00:00', 'Pending', 'Acceptance');
                """);

            await LocalSchemaUpgrade.ApplyAsync(db, CancellationToken.None);

            var milestone = await db.Milestones.SingleAsync();
            Assert.Equal(tenantId, milestone.TenantId);
            Assert.Equal(projectId, milestone.ProjectId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Sqlite_upgrade_adds_and_backfills_canonical_position_paths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"regent-schema-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"Positions\";");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE "Positions" (
                    "Id" TEXT NOT NULL PRIMARY KEY, "TenantId" TEXT NOT NULL,
                    "Name" TEXT NOT NULL, "Responsibilities" TEXT NOT NULL,
                    "CapabilitiesJson" TEXT NOT NULL, "BudgetMinor" INTEGER NOT NULL,
                    "ParentPositionId" TEXT NULL, "Status" TEXT NOT NULL, "Version" INTEGER NOT NULL
                );
                """);
            var tenantId = Guid.NewGuid();
            var otherTenantId = Guid.NewGuid();
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            var otherRootId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Positions"
                    ("Id", "TenantId", "Name", "Responsibilities", "CapabilitiesJson", "BudgetMinor", "ParentPositionId", "Status", "Version")
                VALUES
                    ({rootId}, {tenantId}, 'Root', 'Lead', '[]', 0, NULL, 'Filled', 1),
                    ({childId}, {tenantId}, 'Child', 'Build', '[]', 0, {rootId}, 'Open', 1),
                    ({otherRootId}, {otherTenantId}, 'Other root', 'Lead', '[]', 0, NULL, 'Filled', 1);
                """);

            await LocalSchemaUpgrade.ApplyAsync(db, CancellationToken.None);

            var positions = await db.Positions.ToDictionaryAsync(position => position.Id);
            Assert.Equal(PositionPath.Root(rootId), positions[rootId].Path);
            Assert.Equal(PositionPath.Child(PositionPath.Root(rootId), rootId, childId), positions[childId].Path);
            Assert.Equal(PositionPath.Root(otherRootId), positions[otherRootId].Path);
            Assert.All(positions.Values, position => Assert.NotEqual(Guid.Empty, Guid.ParseExact(position.Path.Split('.')[^1], "N")));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
