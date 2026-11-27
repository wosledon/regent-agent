using RegentAgent.Storage;

namespace RegentAgent.Memory;

public sealed class JsonMemoryVectorStore(JsonDocumentStore<MemoryVectorRecord> documents) : IMemoryVectorStore
{
    public async Task<MemoryVectorRecord?> GetAsync(string tenantId, string claimId,
        CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        _ = MemoryVectorFormat.ValidateClaimId(claimId);
        var record = await documents.GetAsync(claimId, cancellationToken);
        return record is not null && MemoryVectorFormat.ValidateTenantId(record.TenantId) == tenant ? record : null;
    }

    public async Task PutAsync(MemoryVectorRecord record, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateRecord(record);
        var existing = await documents.GetAsync(record.ClaimId, cancellationToken);
        if (existing is not null && MemoryVectorFormat.ValidateTenantId(existing.TenantId) != tenant)
            throw new UnauthorizedAccessException();
        await documents.PutAsync(record.ClaimId, record, cancellationToken);
    }

    public async Task DeleteAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        _ = MemoryVectorFormat.ValidateClaimId(claimId);
        var existing = await documents.GetAsync(claimId, cancellationToken);
        if (existing is not null && MemoryVectorFormat.ValidateTenantId(existing.TenantId) != tenant)
            throw new UnauthorizedAccessException();
        await documents.DeleteAsync(claimId, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryVectorRecord>> GetAllAsync(string tenantId,
        CancellationToken cancellationToken) =>
        GetAllForTenantAsync(MemoryVectorFormat.ValidateTenantId(tenantId), cancellationToken);

    public Task<int> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var id = MemoryVectorFormat.ValidateTenantId(tenantId);
        return documents.DeleteTenantAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<MemoryVectorRecord>> SearchAsync(string tenantId, float[] queryVector,
        IReadOnlyList<string> allowedClaimIds, int maxResults, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        MemoryVectorFormat.ValidateSearch(queryVector, allowedClaimIds, maxResults);
        var allowed = allowedClaimIds.ToHashSet(StringComparer.Ordinal);
        return (await GetAllForTenantAsync(tenant, cancellationToken))
            .Where(record => allowed.Contains(record.ClaimId))
            .OrderByDescending(record => LocalEmbeddingProvider.Cosine(queryVector, record.Vector))
            .ThenBy(record => record.ClaimId, StringComparer.Ordinal)
            .Take(maxResults).ToArray();
    }

    private async Task<IReadOnlyList<MemoryVectorRecord>> GetAllForTenantAsync(Guid tenant,
        CancellationToken cancellationToken) =>
        (await documents.GetAllAsync(cancellationToken))
            .Where(record => MemoryVectorFormat.ValidateTenantId(record.TenantId) == tenant).ToArray();
}
