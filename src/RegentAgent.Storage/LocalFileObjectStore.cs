using System.Security.Cryptography;

namespace RegentAgent.Storage;

public sealed class LocalFileObjectStore : IObjectStore
{
    private readonly string root;

    public LocalFileObjectStore(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }

    public async Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
    {
        var path = ResolvePath(request.ObjectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.upload";
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await request.Content.CopyToAsync(output, cancellationToken);
            }
            var hash = await HashFileAsync(temp, cancellationToken);
            var length = new FileInfo(temp).Length;
            if (request.ExpectedLength is { } expected && expected != length)
            {
                throw new InvalidOperationException("Object length does not match the expected length.");
            }
            File.Move(temp, path, false);
            return new StoredObject(request.ObjectKey, length, request.ContentType, hash, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        return new StoredObject(objectKey, info.Length, "application/octet-stream",
            await HashFileAsync(path, cancellationToken), info.LastWriteTimeUtc);
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        var path = ResolvePath(objectKey);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string ResolvePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || Path.IsPathRooted(objectKey) || objectKey.Contains('\\'))
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }
        var normalized = objectKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Object key escapes the storage root.", nameof(objectKey));
        }
        return path;
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
