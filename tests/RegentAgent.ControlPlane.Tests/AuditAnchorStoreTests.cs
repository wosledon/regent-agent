using RegentAgent.Contracts.Governance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class AuditAnchorStoreTests
{
    [Fact]
    public async Task Object_store_anchor_round_trip_is_tenant_scoped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"anchor-objects-{Guid.NewGuid():N}");
        try
        {
            var store = new ObjectStoreAuditAnchorStore(new LocalFileObjectStore(root));
            var tenantId = Guid.NewGuid();
            var anchor = new AuditAnchorRecord(tenantId, 42, "merkle-root", DateTimeOffset.UtcNow);

            await store.StoreAsync(anchor, CancellationToken.None);
            var restored = await store.GetAsync(tenantId, 42, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal(anchor.MerkleRoot, restored!.MerkleRoot);
            Assert.Null(await store.GetAsync(Guid.NewGuid(), 42, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
