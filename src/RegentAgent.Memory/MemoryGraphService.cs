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

    public async Task<MemoryGraphNode> EnsureClaimNodeAsync(string tenantId, string claimId, string label,
        CancellationToken cancellationToken)
    {
        var id = $"claim:{claimId}";
        var existing = (await nodes.GetAllAsync(cancellationToken)).FirstOrDefault(x =>
            SameTenant(x.TenantId, tenantId) && x.ClaimId == claimId);
        if (existing is not null) return existing;
        var node = new MemoryGraphNode(id, tenantId, claimId, label);
        await nodes.PutAsync(id, node, cancellationToken);
        return node;
    }

    public async Task DeleteClaimAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        var relatedNodes = (await nodes.GetAllAsync(cancellationToken))
            .Where(x => SameTenant(x.TenantId, tenantId) && x.ClaimId == claimId).ToArray();
        foreach (var node in relatedNodes) await nodes.DeleteAsync(node.Id, cancellationToken);
        var relatedEdges = (await edges.GetAllAsync(cancellationToken))
            .Where(x => SameTenant(x.TenantId, tenantId) && (x.FromClaimId == claimId || x.ToClaimId == claimId)).ToArray();
        foreach (var edge in relatedEdges) await edges.DeleteAsync(edge.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryGraphEdge>> GetClaimEdgesAsync(string tenantId, string claimId,
        CancellationToken cancellationToken) =>
        (await edges.GetAllAsync(cancellationToken))
            .Where(x => SameTenant(x.TenantId, tenantId) && (x.FromClaimId == claimId || x.ToClaimId == claimId)).ToArray();

    public async Task<MemoryGraphEdge> AddEdgeAsync(MemoryGraphEdge edge, CancellationToken cancellationToken)
    {
        var allNodes = await nodes.GetAllAsync(cancellationToken);
        if (allNodes.Any(x => SameTenant(x.TenantId, edge.TenantId) && x.ClaimId == edge.FromClaimId)
            && allNodes.Any(x => SameTenant(x.TenantId, edge.TenantId) && x.ClaimId == edge.ToClaimId))
            await edges.PutAsync(edge.Id, edge, cancellationToken);
        else throw new KeyNotFoundException("Both graph nodes must exist in the same tenant.");
        return edge;
    }

    public async Task<MemoryGraphQueryResult> QueryAsync(string tenantId, string startClaimId, int maxDepth,
        int maxNodes, CancellationToken cancellationToken)
    {
        if (maxDepth is < 1 or > 5 || maxNodes is < 1 or > 100) throw new InvalidOperationException("Invalid graph query bounds.");
        var allNodes = (await nodes.GetAllAsync(cancellationToken)).Where(x => SameTenant(x.TenantId, tenantId)).ToArray();
        var allEdges = (await edges.GetAllAsync(cancellationToken)).Where(x => SameTenant(x.TenantId, tenantId)).ToArray();
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

    private static bool SameTenant(string left, string right) =>
        Guid.TryParse(left, out var leftId) && Guid.TryParse(right, out var rightId)
            ? leftId == rightId
            : string.Equals(left, right, StringComparison.Ordinal);
}
