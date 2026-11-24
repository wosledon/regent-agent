using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class S3ObjectStoreTests
{
    [Fact]
    public async Task S3_put_stores_checksum_metadata_and_preserves_bytes()
    {
        var s3 = new Mock<IAmazonS3>();
        string? uploaded = null;
        PutObjectRequest? captured = null;
        s3.Setup(client => client.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) =>
            {
                captured = request;
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8, leaveOpen: true);
                uploaded = reader.ReadToEnd();
            })
            .ReturnsAsync(new PutObjectResponse());
        var options = new S3ObjectStoreOptions
        {
            ServiceUrl = "http://localhost:9000", Bucket = "regent-artifacts", Region = "us-east-1",
            AccessKey = "test", SecretKey = "test"
        };
        var store = new S3ObjectStore(s3.Object, options);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes("hello"));

        var stored = await store.PutAsync(new ObjectUploadRequest("tenant/artifact", input, "text/plain"), CancellationToken.None);

        Assert.Equal("hello", uploaded);
        Assert.Equal("tenant/artifact", captured!.Key);
        Assert.Equal("regent-artifacts", captured.BucketName);
        Assert.False(captured.UseChunkEncoding);
        Assert.Equal(stored.Sha256, captured.Metadata["regent-sha256"]);
        Assert.Equal(64, stored.Sha256.Length);
    }

    [Fact]
    public async Task S3_read_and_delete_use_the_configured_bucket()
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(client => client.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetObjectResponse { ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("content")) });
        s3.Setup(client => client.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteObjectResponse());
        var store = new S3ObjectStore(s3.Object, new S3ObjectStoreOptions { Bucket = "bucket" });

        await using var stream = await store.OpenReadAsync("key", CancellationToken.None);
        using var reader = new StreamReader(stream!);
        Assert.Equal("content", await reader.ReadToEndAsync());
        Assert.True(await store.DeleteAsync("key", CancellationToken.None));
        s3.Verify(client => client.GetObjectAsync(It.Is<GetObjectRequest>(request => request.BucketName == "bucket" && request.Key == "key"),
            It.IsAny<CancellationToken>()), Times.Once);
        s3.Verify(client => client.DeleteObjectAsync(It.Is<DeleteObjectRequest>(request => request.BucketName == "bucket" && request.Key == "key"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task S3_key_traversal_is_rejected_before_the_client_is_called()
    {
        var s3 = new Mock<IAmazonS3>(MockBehavior.Strict);
        var store = new S3ObjectStore(s3.Object, new S3ObjectStoreOptions { Bucket = "bucket" });
        await using var input = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(
            new ObjectUploadRequest("../escape", input, "application/octet-stream"), CancellationToken.None));
        s3.VerifyNoOtherCalls();
    }

    [Fact]
    public void S3_options_require_https_or_an_approved_internal_endpoint()
    {
        var options = new S3ObjectStoreOptions
        {
            ServiceUrl = "http://objects.example.test", Bucket = "bucket", AccessKey = "key", SecretKey = "secret"
        };
        Assert.Throws<InvalidOperationException>(options.Validate);
        options.ServiceUrl = "https://objects.example.test";
        options.Validate();
    }
}
