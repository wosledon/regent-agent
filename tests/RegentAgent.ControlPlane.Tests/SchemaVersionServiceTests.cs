using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class SchemaVersionServiceTests
{
    [Fact]
    public async Task Production_validation_does_not_bootstrap_missing_schema()
    {
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new SchemaVersionService(db, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(CancellationToken.None, false));
        Assert.Empty(db.SchemaVersions);

        await service.ValidateAsync(CancellationToken.None);
        Assert.Equal(SchemaVersionService.CurrentVersion, db.SchemaVersions.Single().Version);
    }

    [Fact]
    public async Task Newer_schema_is_rejected()
    {
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.SchemaVersions.Add(new SchemaVersionRecord
        {
            Version = SchemaVersionService.CurrentVersion + 1, Name = "future", AppliedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = new SchemaVersionService(db, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ValidateAsync(CancellationToken.None));
    }
}
