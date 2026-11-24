using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class AuditIntegrityServiceTests
{
    [Fact]
    public async Task Audit_chain_detects_tampering_and_creates_merkle_checkpoint()
    {
        var root = Path.Combine(Path.GetTempPath(), $"audit-anchors-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var service = new AuditIntegrityService(db, TimeProvider.System, new LocalFileAuditAnchorStore(root));
            await service.BackfillAsync(CancellationToken.None);
            await service.AppendAsync(tenantId, "security.review", "Reviewer", new { result = "pass" }, CancellationToken.None);

            var valid = await service.VerifyAsync(tenantId, CancellationToken.None);
            var checkpoint = await service.CheckpointAsync(tenantId, "anchored", CancellationToken.None);
            var anchors = await service.VerifyAnchorsAsync(tenantId, CancellationToken.None);
            db.AuditRecords.Single(x => x.EventType == "security.review").PayloadJson = "{\"result\":\"tampered\"}";
            await db.SaveChangesAsync();
            var invalid = await service.VerifyAsync(tenantId, CancellationToken.None);
            await service.BackfillAsync(CancellationToken.None);
            var afterRestart = await service.VerifyAsync(tenantId, CancellationToken.None);

            Assert.True(valid.Valid);
            Assert.True(anchors.Valid);
            Assert.Equal(1, anchors.CheckedRecords);
            Assert.False(invalid.Valid);
            Assert.False(afterRestart.Valid);
            Assert.NotNull(invalid.BrokenAtSeq);
            Assert.NotEmpty(checkpoint.MerkleRoot);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
