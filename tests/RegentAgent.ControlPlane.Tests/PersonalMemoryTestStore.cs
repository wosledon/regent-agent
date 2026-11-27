using RegentAgent.Contracts.Memory;
using RegentAgent.Memory;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class PersonalMemoryTestStore : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"regent-personal-memory-{Guid.NewGuid():N}");
    private readonly JsonDocumentStore<MemoryClaim> claims;
    private readonly JsonMemoryVectorStore vectors;

    public PersonalMemoryTestStore(TimeProvider clock)
    {
        claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
        vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
        var findings = new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json"));
        var graph = new MemoryGraphService(
            new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
            new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
        Index = new MemoryIndexService(claims, vectors, findings, graph, clock);
        Memory = new MemoryService(claims,
            new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json")), Index);
    }

    public MemoryIndexService Index { get; }
    public MemoryService Memory { get; }

    public async Task<MemoryClaim> SeedPersonalClaimAsync(Guid tenantId, Guid employeeId, string content,
        DateTimeOffset createdAt, Guid? employmentPeriodId = null, string status = "Active",
        CancellationToken cancellationToken = default)
    {
        var claim = new MemoryClaim(Guid.NewGuid().ToString("N"), tenantId.ToString("D"), "personal", content,
            "source:test", 0.8, "trusted", status, 1, Author: "Employee", CreatedAt: createdAt,
            AuthorEmployeeId: employeeId.ToString("D"),
            AuthorEmploymentPeriodId: employmentPeriodId?.ToString("D"));
        await claims.PutAsync(claim.Id, claim, cancellationToken);
        await Index.IndexClaimAsync(claim, cancellationToken);
        return claim;
    }

    public Task<MemoryClaim?> GetClaimAsync(string claimId, CancellationToken cancellationToken = default) =>
        claims.GetAsync(claimId, cancellationToken);

    public Task<MemoryVectorRecord?> GetVectorAsync(Guid tenantId, string claimId,
        CancellationToken cancellationToken = default) => vectors.GetAsync(tenantId.ToString("D"), claimId,
        cancellationToken);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
