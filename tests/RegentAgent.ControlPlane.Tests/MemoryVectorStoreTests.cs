using System.Text.Json;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class MemoryVectorStoreTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string OtherTenant = "22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task Json_adapter_preserves_document_format_and_tenant_crud()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-vectors-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "memory-vectors.json");
            var documents = new JsonDocumentStore<MemoryVectorRecord>(path);
            var store = new JsonMemoryVectorStore(documents);
            var record = Record("claim-1", Tenant, Axis(0));
            var other = Record("claim-2", OtherTenant, Axis(1));

            await store.PutAsync(record, CancellationToken.None);
            await store.PutAsync(other, CancellationToken.None);

            Assert.Equivalent(record, await store.GetAsync(Tenant, "claim-1", CancellationToken.None));
            Assert.Null(await store.GetAsync(OtherTenant, "claim-1", CancellationToken.None));
            Assert.Single(await store.GetAllAsync(Tenant, CancellationToken.None));
            var persisted = JsonSerializer.Deserialize<Dictionary<string, MemoryVectorRecord>>(
                await File.ReadAllTextAsync(path, CancellationToken.None));
            Assert.NotNull(persisted);
            var stored = persisted["claim-1"];
            Assert.Equal(record.ClaimId, stored.ClaimId);
            Assert.Equal(record.TenantId, stored.TenantId);
            Assert.Equal(record.ContentHash, stored.ContentHash);
            Assert.Equal(record.ModelVersion, stored.ModelVersion);
            Assert.Equal(record.Vector, stored.Vector);
            Assert.Equal(record.IndexedAt, stored.IndexedAt);

            await store.DeleteAsync(Tenant, "claim-1", CancellationToken.None);
            Assert.Null(await store.GetAsync(Tenant, "claim-1", CancellationToken.None));
            Assert.Equal(1, await store.DeleteTenantAsync(OtherTenant, CancellationToken.None));
            Assert.Empty(await documents.GetAllAsync(CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Json_search_is_bounded_and_restricted_to_allowed_claims()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-vector-search-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            await store.PutAsync(Record("orthogonal", Tenant, Axis(1)), CancellationToken.None);
            await store.PutAsync(Record("aligned", Tenant, Axis(0)), CancellationToken.None);
            await store.PutAsync(Record("excluded", Tenant, Axis(0)), CancellationToken.None);
            await store.PutAsync(Record("foreign", OtherTenant, Axis(0)), CancellationToken.None);

            var hits = await store.SearchAsync(Tenant, Axis(0), ["orthogonal", "aligned", "foreign"], 2,
                CancellationToken.None);

            Assert.Equal(["aligned", "orthogonal"], hits.Select(hit => hit.ClaimId));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                store.SearchAsync(Tenant, Axis(0), ["aligned"], 201, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.SearchAsync(Tenant, new float[MemoryVectorFormat.Dimensions - 1], ["aligned"], 1, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Json_adapter_fails_closed_for_invalid_tenant_and_vector_inputs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-vector-validation-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var nonFinite = Axis(0);
            nonFinite[0] = float.PositiveInfinity;

            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(
                Record("claim", "invalid", Axis(0)), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(
                Record("claim", Tenant, new float[MemoryVectorFormat.Dimensions - 1]), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(
                Record("claim", Tenant, nonFinite), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync("invalid", "claim", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(Tenant, "", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(
                Tenant, new float[MemoryVectorFormat.Dimensions - 1], ["claim"], 1, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteTenantAsync("invalid", CancellationToken.None));
            Assert.Empty(await store.GetAllAsync(Tenant, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Json_adapter_rejects_cross_tenant_claim_id_reuse_and_delete()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-vector-isolation-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            await store.PutAsync(Record("shared", Tenant, Axis(0)), CancellationToken.None);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.PutAsync(Record("shared", OtherTenant, Axis(1)), CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.DeleteAsync(OtherTenant, "shared", CancellationToken.None));
            Assert.NotNull(await store.GetAsync(Tenant, "shared", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Json_adapter_treats_guid_text_forms_as_the_same_tenant_without_rewriting_other_records()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-vector-guids-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(root, "vectors.json");
            var documents = new JsonDocumentStore<MemoryVectorRecord>(path);
            var store = new JsonMemoryVectorStore(documents);
            var alternateTenant = $"{{{Tenant.ToUpperInvariant()}}}";
            var compactTenant = Tenant.Replace("-", "").ToUpperInvariant();
            var original = Record("claim", Tenant, Axis(0));
            var unrelated = Record("unrelated", OtherTenant, Axis(1));
            await store.PutAsync(original, CancellationToken.None);
            await store.PutAsync(unrelated, CancellationToken.None);

            Assert.Equivalent(original, await store.GetAsync(alternateTenant, "claim", CancellationToken.None));
            Assert.Single(await store.GetAllAsync(compactTenant, CancellationToken.None));
            Assert.Single(await store.SearchAsync(alternateTenant, Axis(0), ["claim"], 1, CancellationToken.None));

            var updated = original with { TenantId = compactTenant, ContentHash = "updated" };
            await store.PutAsync(updated, CancellationToken.None);
            Assert.Equal(compactTenant, (await documents.GetAsync("claim", CancellationToken.None))!.TenantId);
            Assert.Equivalent(unrelated, await documents.GetAsync("unrelated", CancellationToken.None));

            await store.PutAsync(Record("delete-me", alternateTenant, Axis(0)), CancellationToken.None);
            await store.DeleteAsync(compactTenant, "delete-me", CancellationToken.None);
            Assert.Null(await store.GetAsync(Tenant, "delete-me", CancellationToken.None));
            Assert.Equal(1, await store.DeleteTenantAsync(alternateTenant, CancellationToken.None));
            Assert.Null(await store.GetAsync(Tenant, "claim", CancellationToken.None));
            Assert.Equivalent(unrelated, await documents.GetAsync("unrelated", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static MemoryVectorRecord Record(string claimId, string tenantId, float[] vector) =>
        new(claimId, tenantId, $"hash-{claimId}", "test-v1", vector, DateTimeOffset.UnixEpoch);

    private static float[] Axis(int axis)
    {
        var vector = new float[MemoryVectorFormat.Dimensions];
        vector[axis] = 1;
        return vector;
    }
}
