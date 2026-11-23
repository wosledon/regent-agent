using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Runtime;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TaskExecutionServiceTests
{
    [Fact]
    public async Task Runner_can_claim_checkpoint_and_complete_task()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-task-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            await audit.BackfillAsync(CancellationToken.None);
            var service = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit);
            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var claim = await service.ClaimAsync("runner-test", tenantId, CancellationToken.None);
            Assert.NotNull(claim);
            Assert.Equal("Generate local delivery summary", claim.Title);
            Assert.True(await service.CheckpointAsync(claim.TaskId, new TaskCheckpoint(claim.LeaseToken, "{}"), tenantId, CancellationToken.None));
            Assert.True(await service.CompleteAsync(claim.TaskId,
                new TaskCompletion(claim.LeaseToken, "# Result", "Done", ["test:local"]), tenantId, CancellationToken.None));

            var task = await db.Tasks.SingleAsync(x => x.Id == claim.TaskId);
            Assert.Equal("Done", task.Status);
            Assert.Null(task.LeaseToken);
            Assert.Single(await db.Artifacts.ToListAsync());
            Assert.Contains(await db.AuditRecords.ToListAsync(), x => x.EventType == "task.completed");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
