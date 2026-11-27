namespace RegentAgent.Storage;

public sealed record ObjectUploadRequest(
    string ObjectKey,
    Stream Content,
    string ContentType,
    long? ExpectedLength = null);

public sealed record StoredObject(
    string ObjectKey,
    long Length,
    string ContentType,
    string Sha256,
    DateTimeOffset StoredAt);

public interface IObjectStore
{
    Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken);
    Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken);
    Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken);
    Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken);
}
