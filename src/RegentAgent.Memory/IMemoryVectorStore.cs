namespace RegentAgent.Memory;

public interface IMemoryVectorStore
{
    Task<MemoryVectorRecord?> GetAsync(string tenantId, string claimId, CancellationToken cancellationToken);
    Task PutAsync(MemoryVectorRecord record, CancellationToken cancellationToken);
    Task DeleteAsync(string tenantId, string claimId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryVectorRecord>> GetAllAsync(string tenantId, CancellationToken cancellationToken);
    Task<int> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MemoryVectorRecord>> SearchAsync(string tenantId, float[] queryVector,
        IReadOnlyList<string> allowedClaimIds, int maxResults, CancellationToken cancellationToken);
}
