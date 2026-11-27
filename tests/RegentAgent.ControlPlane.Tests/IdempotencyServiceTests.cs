using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task Completed_same_hash_is_a_no_op_and_never_overwrites_the_response()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(db, TimeProvider.System);

        await service.SaveCompletedAsync(TenantId, "key", "operation", new { value = "first" },
            CancellationToken.None, "hash");
        await service.SaveCompletedAsync(TenantId, "key", "operation", new { value = "second" },
            CancellationToken.None, "hash");

        var record = await db.IdempotencyRecords.SingleAsync();
        Assert.Contains("first", record.ResponseJson);
        Assert.DoesNotContain("second", record.ResponseJson);
    }

    [Fact]
    public async Task Completed_mismatched_hash_conflicts()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(db, TimeProvider.System);
        await service.SaveCompletedAsync(TenantId, "key", "operation", new { value = "first" },
            CancellationToken.None, "hash");

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => service.SaveCompletedAsync(
            TenantId, "key", "operation", new { value = "second" }, CancellationToken.None, "other"));
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("Failed")]
    public async Task Matching_in_progress_or_failed_row_can_complete(string status)
    {
        await using var db = CreateDb();
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Key = "key", Operation = "operation",
            RequestHash = "hash", Status = status, ResponseJson = "null"
        });
        await db.SaveChangesAsync();
        var service = new IdempotencyService(db, TimeProvider.System);

        await service.SaveCompletedAsync(TenantId, "key", "operation", new { value = "done" },
            CancellationToken.None, "hash");

        Assert.Equal("Completed", (await db.IdempotencyRecords.SingleAsync()).Status);
    }

    [Fact]
    public async Task Staged_completion_is_flushed_by_the_callers_single_save()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(db, TimeProvider.System);

        await service.SaveCompletedAsync(TenantId, "key", "operation", new { value = "done" },
            CancellationToken.None, "hash", saveChanges: false);
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        await db.SaveChangesAsync();

        Assert.Equal("Completed", (await db.IdempotencyRecords.SingleAsync()).Status);
    }

    [Fact]
    public void Unique_tenant_key_operation_index_is_preserved()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(IdempotencyRecord))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(x => x.Name).SequenceEqual(["TenantId", "Key", "Operation"]));
    }

    private static readonly Guid TenantId = Guid.NewGuid();

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
