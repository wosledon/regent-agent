using RegentAgent.Contracts.Memory;
using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed class MemoryGraphService(
    JsonDocumentStore<MemoryGraphNode> nodes,
    JsonDocumentStore<MemoryGraphEdge> edges)
{
    public async Task<MemoryGraphNode> AddNodeAsync(MemoryGraphNode node, CancellationToken cancellationToken)
    {
        await nodes.PutAsync(node.Id, node, cancellationToken);
        return node;
    }

    public async Task<MemoryGraphEdge> AddEdgeAsync(MemoryGraphEdge edge, CancellationToken cancellationToken)
    {
        var allNodes = await nodes.GetAllAsync(cancellationToken);
        if (allNodes.Any(x => x.TenantId == edge.TenantId && x.ClaimId == edge.FromClaimId)
            && allNodes.Any(x => x.TenantId == edge.TenantId && x.ClaimId == edge.ToClaimId))
            await edges.PutAsync(edge.Id, edge, cancellationToken);
        else throw new KeyNotFoundException("Both graph nodes must exist in the same tenant.");
        return edge;
    }

    public async Task<MemoryGraphQueryResult> QueryAsync(string tenantId, string startClaimId, int maxDepth,
        int maxNodes, CancellationToken cancellationToken)
    {
        if (maxDepth is < 1 or > 5 || maxNodes is < 1 or > 100) throw new InvalidOperationException("Invalid graph query bounds.");
        var allNodes = (await nodes.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();
        var allEdges = (await edges.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(string ClaimId, int Depth)>();
        queue.Enqueue((startClaimId, 0));
        while (queue.Count > 0 && visited.Count < maxNodes)
        {
            var (claimId, depth) = queue.Dequeue();
            if (!visited.Add(claimId)) continue;
            if (depth >= maxDepth) continue;
            foreach (var edge in allEdges.Where(x => x.FromClaimId == claimId || x.ToClaimId == claimId))
            {
                var next = edge.FromClaimId == claimId ? edge.ToClaimId : edge.FromClaimId;
                queue.Enqueue((next, depth + 1));
            }
        }
        return new MemoryGraphQueryResult(
            allNodes.Where(x => visited.Contains(x.ClaimId)).ToArray(),
            allEdges.Where(x => visited.Contains(x.FromClaimId) && visited.Contains(x.ToClaimId)).ToArray());
    }
}
