namespace RegentAgent.Storage;

public interface IDocumentStore<T> where T : class
{
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken);
    Task<T?> GetAsync(string id, CancellationToken cancellationToken);
    Task<T> GetOrAddAsync(string id, Func<T> valueFactory, CancellationToken cancellationToken);
    Task<T> MutateAsync(string id, Func<T?, T> mutation, CancellationToken cancellationToken);
    Task<T> MutateAsync(string id, Func<T?, Task<T>> mutation, CancellationToken cancellationToken);
    Task PutAsync(string id, T value, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
    Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
