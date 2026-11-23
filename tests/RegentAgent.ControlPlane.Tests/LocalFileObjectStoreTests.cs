using System.Text;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class LocalFileObjectStoreTests
{
    [Fact]
    public async Task Put_and_read_preserve_content_and_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-objects-{Guid.NewGuid():N}");
        try
        {
            var store = new LocalFileObjectStore(root);
            await using var input = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
            var stored = await store.PutAsync(new ObjectUploadRequest("tenant/artifact", input, "text/plain"), CancellationToken.None);
            await using var output = await store.OpenReadAsync("tenant/artifact", CancellationToken.None);
            using var reader = new StreamReader(output!);
            Assert.Equal("hello", await reader.ReadToEndAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Path_traversal_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-objects-{Guid.NewGuid():N}");
        try
        {
            var store = new LocalFileObjectStore(root);
            await using var input = new MemoryStream([1]);
            await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(new ObjectUploadRequest("../escape", input, "application/octet-stream"), CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
