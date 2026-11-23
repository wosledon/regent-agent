namespace RegentAgent.Contracts.Memory;

public sealed record MemoryGraphNode(string Id, string TenantId, string ClaimId, string Label);
public sealed record MemoryGraphEdge(string Id, string TenantId, string FromClaimId, string ToClaimId, string Relation);
public sealed record MemoryGraphQueryResult(IReadOnlyList<MemoryGraphNode> Nodes, IReadOnlyList<MemoryGraphEdge> Edges);
