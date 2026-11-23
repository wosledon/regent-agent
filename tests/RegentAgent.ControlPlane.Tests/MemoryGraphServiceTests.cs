using RegentAgent.Contracts.Memory;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class MemoryGraphServiceTests
{
    [Fact]
    public async Task Graph_query_is_bounded_and_tenant_scoped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-graph-{Guid.NewGuid():N}");
        try
        {
            var service = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            const string tenant = "tenant-a";
            await service.AddNodeAsync(new MemoryGraphNode("n1", tenant, "claim-1", "One"), CancellationToken.None);
            await service.AddNodeAsync(new MemoryGraphNode("n2", tenant, "claim-2", "Two"), CancellationToken.None);
            await service.AddNodeAsync(new MemoryGraphNode("n3", "tenant-b", "claim-3", "Other"), CancellationToken.None);
            await service.AddEdgeAsync(new MemoryGraphEdge("e1", tenant, "claim-1", "claim-2", "supports"), CancellationToken.None);
            var result = await service.QueryAsync(tenant, "claim-1", 2, 10, CancellationToken.None);
            Assert.Equal(2, result.Nodes.Count);
            Assert.Single(result.Edges);
            Assert.DoesNotContain(result.Nodes, x => x.ClaimId == "claim-3");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
