namespace RegentAgent.Storage;

public interface ITenantScopedDocument
{
    Guid TenantId { get; }
}

public interface ITenantJsonDocumentStore
{
    Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}
