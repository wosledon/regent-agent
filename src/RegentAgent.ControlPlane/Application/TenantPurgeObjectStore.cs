using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantPurgeObjectStore(IObjectStore inner, TenantObjectUploadTracker uploads,
    ITenantPurgeStateProvider? purgeState = null) : IObjectStore
{
    public async Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(request.ObjectKey, out var tenantId))
            return await inner.PutAsync(request, cancellationToken);

        await TenantPurgeFence.CheckPersistedPurgeAsync(tenantId, cancellationToken, purgeState);
        using var writeAccess = await TenantPurgeFence.EnterWriteAsync(tenantId, cancellationToken);
        using var upload = uploads.Track(tenantId);
        return await inner.PutAsync(request, cancellationToken);
    }

    public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
        inner.GetMetadataAsync(objectKey, cancellationToken);

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
        inner.OpenReadAsync(objectKey, cancellationToken);

    public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
        inner.DeleteAsync(objectKey, cancellationToken);

    public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
        inner.DeletePrefixAsync(prefix, cancellationToken);

    public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
        inner.IsPrefixEmptyAsync(prefix, cancellationToken);

    private static bool TryGetTenantId(string objectKey, out Guid tenantId) =>
        TenantObjectNamespace.TryGetTenantId(objectKey, out tenantId);
}
