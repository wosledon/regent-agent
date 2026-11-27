using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PositionPathTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Position_path_column_type_follows_the_relational_provider()
    {
        using var sqlite = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        using var postgres = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Timeout=1").Options);

        Assert.Equal("text", sqlite.Model.FindEntityType(typeof(PositionRecord))!.FindProperty(nameof(PositionRecord.Path))!.GetColumnType());
        Assert.Equal("ltree", postgres.Model.FindEntityType(typeof(PositionRecord))!.FindProperty(nameof(PositionRecord.Path))!.GetColumnType());
    }

    [Fact]
    public void Paths_use_canonical_root_child_sibling_and_rebased_labels()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var siblingId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();
        var root = PositionPath.Root(rootId);
        var child = PositionPath.Child(root, rootId, childId);

        Assert.Equal($"root.{rootId:N}", root);
        Assert.Equal($"root.{rootId:N}.{childId:N}", child);
        Assert.Equal($"root.{rootId:N}.{siblingId:N}", PositionPath.Sibling(child, childId, siblingId));
        Assert.Equal(PositionPath.Root(siblingId), PositionPath.Rebase(child, PositionPath.Root(siblingId), child));
        Assert.Equal($"{PositionPath.Root(siblingId)}.{grandchildId:N}",
            PositionPath.Rebase(child, PositionPath.Root(siblingId), $"{child}.{grandchildId:N}"));
        Assert.Equal(2, PositionPath.Depth(child));
    }

    [Fact]
    public void Build_paths_rejects_cycles_and_cross_tenant_parents()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var cycle = new[]
        {
            Position(first, TenantId, second),
            Position(second, TenantId, first)
        };

        Assert.Throws<InvalidOperationException>(() => PositionPath.BuildPaths(cycle));
        var crossTenant = new[]
        {
            Position(first, TenantId, null),
            Position(second, Guid.NewGuid(), first)
        };
        Assert.Throws<InvalidOperationException>(() => PositionPath.BuildPaths(crossTenant));
    }

    [Fact]
    public void Build_paths_and_rebase_enforce_four_position_levels()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var positions = ids.Select((id, index) => Position(id, TenantId, index == 0 ? null : ids[index - 1])).ToArray();

        Assert.Throws<InvalidOperationException>(() => PositionPath.BuildPaths(positions));
        var source = PositionPath.Root(ids[0]);
        var target = $"{PositionPath.Root(ids[0])}.{Guid.NewGuid():N}.{Guid.NewGuid():N}";
        var descendant = $"{source}.{ids[1]:N}.{ids[2]:N}";
        Assert.Throws<InvalidOperationException>(() => PositionPath.Rebase(source, target, descendant));
    }

    private static PositionRecord Position(Guid id, Guid tenantId, Guid? parentId) =>
        new() { Id = id, TenantId = tenantId, ParentPositionId = parentId };
}
