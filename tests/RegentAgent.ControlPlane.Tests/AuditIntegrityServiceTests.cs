using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public async Task Audit_append_normalizes_timestamp_to_postgres_microsecond_precision()
    {
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await DbInitializer.InitializeAsync(db, CancellationToken.None);
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_567);
        var service = new AuditIntegrityService(db, new FixedTimeProvider(now));
        await service.BackfillAsync(CancellationToken.None);

        var record = await service.AppendAsync(tenantId, "security.review", "Reviewer", new { result = "pass" },
            CancellationToken.None);

        Assert.Equal(0, record.CreatedAt.Ticks % 10);
        Assert.True((await service.VerifyAsync(tenantId, CancellationToken.None)).Valid);
    }

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

    [PostgresMigrationFact]
    public async Task Postgres_audit_chain_verifies_pre_fix_timestamp_rows_and_new_suffix()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        await using var schema = await PostgresTemporarySchema.CreateAsync(dsn);
        foreach (var version in Enumerable.Range(3, 7)) await schema.ApplyMigrationAsync(version);
        var tenantId = Guid.NewGuid();
        await schema.InsertTenantAsync(tenantId);
        var storedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_570);
        var originalAt = storedAt.AddTicks(-3);
        const string eventType = "legacy.security.review";
        const string actor = "Legacy Reviewer";
        const string payloadJson = "{\"result\":\"legacy\"}";
        var legacyHash = ComputeAuditHash(tenantId, 1, eventType, actor, payloadJson, string.Empty, originalAt);
        await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql(schema.ConnectionString).Options);
        var legacy = new AuditRecord
        {
            TenantId = tenantId, Seq = 1, EventType = eventType, Actor = actor,
            PayloadJson = payloadJson, PreviousHash = string.Empty, Hash = legacyHash, CreatedAt = storedAt
        };
        db.AuditRecords.Add(legacy);
        await db.SaveChangesAsync();
        var service = new AuditIntegrityService(db, new FixedTimeProvider(storedAt.AddTicks(7)));

        await service.BackfillAsync(CancellationToken.None);
        var verified = await service.VerifyAsync(tenantId, CancellationToken.None);
        var strict = await service.VerifyAsync(tenantId, CancellationToken.None, allowLegacyPostgresPrecision: false);
        await service.AppendAsync(tenantId, "security.review", "Reviewer", new { result = "pass" }, CancellationToken.None);
        var withSuffix = await service.VerifyAsync(tenantId, CancellationToken.None);

        Assert.True(verified.Valid);
        Assert.Contains("legacy PostgreSQL timestamp", verified.Message, StringComparison.Ordinal);
        Assert.False(strict.Valid);
        Assert.True(withSuffix.Valid);
        legacy.PayloadJson = "{\"result\":\"tampered\"}";
        await db.SaveChangesAsync();
        Assert.False((await service.VerifyAsync(tenantId, CancellationToken.None)).Valid);
    }

    private static string ComputeAuditHash(Guid tenantId, long seq, string eventType, string actor, string payloadJson,
        string previousHash, DateTimeOffset createdAt)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        var canonical = JsonSerializer.Serialize(new
        {
            tenant_id = tenantId, seq, event_type = eventType, actor, payload_json = payloadJson,
            previous_hash = previousHash, created_at = createdAt
        }, options);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
