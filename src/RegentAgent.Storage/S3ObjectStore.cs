using System.Net;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;

namespace RegentAgent.Storage;

public sealed class S3ObjectStoreOptions
{
    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxRetries { get; set; } = 2;

    public void Validate()
    {
        if (!Uri.TryCreate(ServiceUrl, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("S3 service URL is invalid.");
        var loopback = uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        var internalHost = uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || uri.Host is "rustfs" or "minio" or "localstack";
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && (loopback || internalHost || IsPrivateAddress(uri.Host))))
            throw new InvalidOperationException("S3 service URL must use HTTPS or an approved internal HTTP endpoint.");
        if (string.IsNullOrWhiteSpace(Region) || string.IsNullOrWhiteSpace(Bucket)
            || string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
            throw new InvalidOperationException("S3 region, bucket and credentials are required.");
        if (TimeoutSeconds is < 1 or > 600 || MaxRetries is < 0 or > 5)
            throw new InvalidOperationException("S3 HTTP limits are invalid.");
    }

    private static bool IsPrivateAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address)) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] is 10 or 127 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}

public sealed class S3ObjectStore(IAmazonS3 client, S3ObjectStoreOptions options) : IObjectStore
{
    private const string HashMetadataKey = "regent-sha256";
    private readonly IAmazonS3 client = client;
    private readonly S3ObjectStoreOptions options = options;

    public static IObjectStore Create(S3ObjectStoreOptions options)
    {
        options.Validate();
        var config = new AmazonS3Config
        {
            ServiceURL = options.ServiceUrl,
            RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region),
            ForcePathStyle = options.ForcePathStyle,
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            MaxErrorRetry = options.MaxRetries
        };
        return new S3ObjectStore(new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(options.AccessKey, options.SecretKey), config), options);
    }

    public async Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
    {
        ValidateKey(request.ObjectKey);
        var temp = Path.Combine(Path.GetTempPath(), $"regent-s3-{Guid.NewGuid():N}.upload");
        try
        {
            await using (var output = File.Create(temp))
                await request.Content.CopyToAsync(output, cancellationToken);
            var length = new FileInfo(temp).Length;
            if (request.ExpectedLength is { } expected && expected != length)
                throw new InvalidOperationException("Object length does not match the expected length.");
            await using var input = File.OpenRead(temp);
            using var hash = SHA256.Create();
            var checksum = Convert.ToHexString(await hash.ComputeHashAsync(input, cancellationToken)).ToLowerInvariant();
            input.Position = 0;
            var put = new PutObjectRequest
            {
                BucketName = options.Bucket,
                Key = request.ObjectKey,
                InputStream = input,
                ContentType = request.ContentType,
                AutoCloseStream = false
            };
            put.Metadata.Add(HashMetadataKey, checksum);
            await client.PutObjectAsync(put, cancellationToken);
            return new StoredObject(request.ObjectKey, length, request.ContentType, checksum, DateTimeOffset.UtcNow);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        try
        {
            var response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = options.Bucket, Key = objectKey
            }, cancellationToken);
            var checksum = response.Metadata.Keys.FirstOrDefault(key => key.Equals(HashMetadataKey, StringComparison.OrdinalIgnoreCase)) is { } metadataKey
                ? response.Metadata[metadataKey] : string.Empty;
            if (string.IsNullOrEmpty(checksum))
            {
                await using var stream = await OpenReadAsync(objectKey, cancellationToken) ?? throw new InvalidOperationException("Object disappeared while reading metadata.");
                checksum = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            }
            return new StoredObject(objectKey, response.ContentLength, response.Headers.ContentType ?? "application/octet-stream",
                checksum, response.LastModified?.ToUniversalTime() ?? DateTimeOffset.UtcNow);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        try
        {
            var response = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = options.Bucket, Key = objectKey
            }, cancellationToken);
            return new OwnerDisposingStream(response.ResponseStream, response);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        try
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = options.Bucket, Key = objectKey }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static void ValidateKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || objectKey.StartsWith('/') || objectKey.Contains('\\')
            || objectKey.Split('/').Any(segment => segment is "." or ".."))
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
    }

    private sealed class OwnerDisposingStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
