using RegentAgent.Contracts.Memory;
using RegentAgent.Memory;
using Moq;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class MemoryIndexServiceTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Hybrid_recall_searches_only_the_permission_filtered_candidate_set()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-index-{Guid.NewGuid():N}");
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var findings = new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json"));
            var jsonVectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var vectors = new RecordingVectorStore(jsonVectors);
            var graph = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            var index = new MemoryIndexService(claims, vectors, findings, graph, TimeProvider.System);
            var organization = Claim("organization", Tenant, "organization", "Boss");
            var alicePersonal = Claim("alice-personal", Tenant, "personal", "Alice");
            var bobPersonal = Claim("bob-personal", Tenant, "personal", "Bob");
            var foreign = Claim("foreign", OtherTenant, "organization", "Boss");
            foreach (var claim in new[] { organization, alicePersonal, bobPersonal, foreign })
                await claims.PutAsync(claim.Id, claim, CancellationToken.None);

            var result = await index.SearchHybridAsync(Tenant, "shared launch", null, 10, "Alice",
                CancellationToken.None, viewerUserId: "user-Alice");

            Assert.Equal(2, result.Considered);
            Assert.NotNull(vectors.LastTenantId);
            Assert.Equal(Tenant, vectors.LastTenantId);
            Assert.NotNull(vectors.LastAllowedClaimIds);
            Assert.Equal(new[] { organization.Id, alicePersonal.Id }.OrderBy(id => id, StringComparer.Ordinal),
                vectors.LastAllowedClaimIds!.OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal(2, vectors.LastMaxResults);
            Assert.All(result.Hits, hit => Assert.Contains(hit.ClaimId, new[] { organization.Id, alicePersonal.Id }));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Alternate_guid_forms_share_index_rebuild_conflicts_graph_findings_and_replacement()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-index-guids-{Guid.NewGuid():N}");
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var findings = new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var graph = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            var index = new MemoryIndexService(claims, vectors, findings, graph, TimeProvider.System);
            var bracedTenant = $"{{{Tenant.ToUpperInvariant()}}}";
            var compactTenant = Tenant.Replace("-", "").ToUpperInvariant();
            var organization = Claim("a-org", bracedTenant, "organization", "Boss");
            var alice = Claim("b-alice", compactTenant, "personal", "Alice");
            var bob = Claim("c-bob", Tenant, "personal", "Bob");
            var conflict = Claim("d-conflict", bracedTenant, "organization", "Boss", "Shared launch checklist updated.");
            var foreign = Claim("foreign", OtherTenant, "organization", "Boss");
            foreach (var claim in new[] { organization, alice, bob, conflict, foreign })
                await claims.PutAsync(claim.Id, claim, CancellationToken.None);
            await index.IndexClaimAsync(organization, CancellationToken.None);
            var reindexed = organization with { TenantId = compactTenant, Content = "Shared launch checklist updated" };
            await index.IndexClaimAsync(reindexed, CancellationToken.None);
            await claims.PutAsync(reindexed.Id, reindexed, CancellationToken.None);
            await index.IndexClaimAsync(foreign, CancellationToken.None);
            Assert.Single(await vectors.GetAllAsync(Tenant, CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(OtherTenant, CancellationToken.None));

            await findings.PutAsync("finding", new MemoryFinding("finding", compactTenant, organization.Id,
                "synthetic.review", "Low", "Synthetic review", [], "Open", DateTimeOffset.UtcNow), CancellationToken.None);
            Assert.Equal(4, await index.RebuildAsync(bracedTenant, CancellationToken.None));
            await graph.AddEdgeAsync(new MemoryGraphEdge("edge", bracedTenant, organization.Id, alice.Id, "supports"), CancellationToken.None);
            Assert.Equal([conflict.Id], await index.FindConflictsAsync(organization.Id, compactTenant, CancellationToken.None));

            var result = await index.SearchHybridAsync(compactTenant, "Shared launch", null, 10, "Alice",
                CancellationToken.None, viewerUserId: "user-Alice");
            Assert.Equal(3, result.Considered);
            Assert.Contains(result.Hits, hit => hit.ClaimId == organization.Id && hit.ReviewRequired && hit.FindingRules.Contains("synthetic.review"));
            var aliceHit = Assert.Single(result.Hits, hit => hit.ClaimId == alice.Id);
            Assert.True(aliceHit.GraphExpanded);
            Assert.DoesNotContain(result.Hits, hit => hit.ClaimId == bob.Id || hit.ClaimId == foreign.Id);

            var corrected = Claim("e-corrected", bracedTenant, "organization", "Boss", "Shared launch checklist corrected");
            await index.ReplaceClaimAsync(compactTenant, organization.Id, corrected, CancellationToken.None);
            Assert.Null(await vectors.GetAsync(Tenant, organization.Id, CancellationToken.None));
            Assert.NotNull(await vectors.GetAsync(compactTenant, corrected.Id, CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(OtherTenant, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Hybrid_recall_propagates_vector_store_failures_without_json_fallback()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-index-failure-{Guid.NewGuid():N}");
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            await claims.PutAsync("claim", Claim("claim", Tenant, "organization", "Boss"), CancellationToken.None);
            var failure = new InvalidOperationException("synthetic vector backend failure");
            var vectors = new Mock<IMemoryVectorStore>();
            vectors.Setup(store => store.GetAsync(Tenant, "claim", It.IsAny<CancellationToken>()))
                .ReturnsAsync((MemoryVectorRecord?)null);
            vectors.Setup(store => store.PutAsync(It.IsAny<MemoryVectorRecord>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            vectors.Setup(store => store.SearchAsync(It.IsAny<string>(), It.IsAny<float[]>(),
                    It.IsAny<IReadOnlyList<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(failure);
            var graph = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            var index = new MemoryIndexService(claims, vectors.Object,
                new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json")), graph, TimeProvider.System);

            var error = await Record.ExceptionAsync(() => index.SearchHybridAsync(
                Tenant, "shared launch", null, 10, "Boss", CancellationToken.None));

            Assert.Same(failure, error);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Rebuild_deletes_only_the_requested_tenant_vectors()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-rebuild-{Guid.NewGuid():N}");
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var graph = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            var index = new MemoryIndexService(claims, vectors,
                new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json")), graph, TimeProvider.System);
            var active = Claim("active", Tenant, "organization", "Boss");
            await claims.PutAsync(active.Id, active, CancellationToken.None);
            await index.IndexClaimAsync(Claim("foreign", OtherTenant, "organization", "Boss"), CancellationToken.None);

            Assert.Equal(1, await index.RebuildAsync(Tenant, CancellationToken.None));

            Assert.Single(await vectors.GetAllAsync(Tenant, CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(OtherTenant, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static MemoryClaim Claim(string id, string tenantId, string scope, string author,
        string content = "Shared launch checklist") =>
        new(id, tenantId, scope, content, "source:test", 0.9, "trusted", "Active", 1,
            Author: author, Verified: true, CreatedAt: DateTimeOffset.UtcNow,
            AuthorUserId: scope == "personal" ? $"user-{author}" : null);

    private sealed class RecordingVectorStore(IMemoryVectorStore inner) : IMemoryVectorStore
    {
        public string? LastTenantId { get; private set; }
        public IReadOnlyList<string>? LastAllowedClaimIds { get; private set; }
        public int LastMaxResults { get; private set; }

        public Task<MemoryVectorRecord?> GetAsync(string tenantId, string claimId, CancellationToken cancellationToken) =>
            inner.GetAsync(tenantId, claimId, cancellationToken);
        public Task PutAsync(MemoryVectorRecord record, CancellationToken cancellationToken) => inner.PutAsync(record, cancellationToken);
        public Task DeleteAsync(string tenantId, string claimId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(tenantId, claimId, cancellationToken);
        public Task<IReadOnlyList<MemoryVectorRecord>> GetAllAsync(string tenantId, CancellationToken cancellationToken) =>
            inner.GetAllAsync(tenantId, cancellationToken);
        public Task<int> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken) =>
            inner.DeleteTenantAsync(tenantId, cancellationToken);

        public Task<IReadOnlyList<MemoryVectorRecord>> SearchAsync(string tenantId, float[] queryVector,
            IReadOnlyList<string> allowedClaimIds, int maxResults, CancellationToken cancellationToken)
        {
            LastTenantId = tenantId;
            LastAllowedClaimIds = allowedClaimIds.ToArray();
            LastMaxResults = maxResults;
            return inner.SearchAsync(tenantId, queryVector, allowedClaimIds, maxResults, cancellationToken);
        }
    }
}
