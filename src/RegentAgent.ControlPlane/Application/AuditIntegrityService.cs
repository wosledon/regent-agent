using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class AuditIntegrityService(RegentDbContext db, TimeProvider clock,
    IAuditAnchorStore? anchors = null)
{
    private readonly IAuditAnchorStore anchors = anchors ?? new LocalFileAuditAnchorStore("data/audit-anchors");
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private static readonly SemaphoreSlim InMemoryAuditGate = new(1, 1);
    private const int TicksPerMicrosecond = 10;
    private static readonly int[] LegacyTimestampOffsets =
        Enumerable.Range(1, 9).Concat(Enumerable.Range(1, 9).Select(offset => -offset)).ToArray();

    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        var tenantIds = await db.AuditRecords.Select(x => x.TenantId).Distinct().ToListAsync(cancellationToken);
        foreach (var tenantId in tenantIds)
        {
            var records = await db.AuditRecords.Where(x => x.TenantId == tenantId).OrderBy(x => x.Seq).ToListAsync(cancellationToken);
            string previous = string.Empty;
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.Hash))
                {
                    record.PreviousHash = previous;
                    record.Hash = ComputeHash(record, previous);
                }
                else if (record.PreviousHash != previous ||
                         !VerifyHash(record, previous, db.Database.IsNpgsql(), out _))
                {
                    break;
                }
                previous = record.Hash;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditRecord> AppendAsync(Guid tenantId, string eventType, string actor, object payload,
        CancellationToken cancellationToken, bool saveChanges = true)
    {
        var relational = db.Database.IsRelational();
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        var gateAcquired = false;
        try
        {
            if (relational && db.Database.CurrentTransaction is null && saveChanges)
                transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (relational && db.Database.CurrentTransaction is not null)
                await AcquireTenantLockAsync(tenantId, cancellationToken);
            else
            {
                await InMemoryAuditGate.WaitAsync(cancellationToken);
                gateAcquired = true;
            }

            var previous = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.Seq).FirstOrDefaultAsync(cancellationToken);
            var record = new AuditRecord
            {
                TenantId = tenantId, Seq = (previous?.Seq ?? 0) + 1,
                EventType = eventType, Actor = actor,
                PayloadJson = JsonSerializer.Serialize(payload, CanonicalOptions),
                CreatedAt = RoundToPostgresPrecision(clock.GetUtcNow()), PreviousHash = previous?.Hash ?? string.Empty
            };
            record.Hash = ComputeHash(record, record.PreviousHash);
            db.AuditRecords.Add(record);
            if (saveChanges) await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return record;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
            if (gateAcquired) InMemoryAuditGate.Release();
        }
    }

    private async Task AcquireTenantLockAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({tenantId.ToString("D")}))", cancellationToken);
            return;
        }
        await db.Tenants.Where(x => x.Id == tenantId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Name, x => x.Name), cancellationToken);
    }

    public async Task<AuditVerificationDto> VerifyAsync(Guid tenantId, CancellationToken cancellationToken,
        bool allowLegacyPostgresPrecision = true)
    {
        var records = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Seq).ToListAsync(cancellationToken);
        var allowLegacy = allowLegacyPostgresPrecision && db.Database.IsNpgsql();
        string previous = string.Empty;
        long expectedSeq = 1;
        var legacyRecords = 0;
        foreach (var record in records)
        {
            if (record.Seq != expectedSeq || record.PreviousHash != previous ||
                !VerifyHash(record, previous, allowLegacy, out var usedLegacy))
                return new AuditVerificationDto(false, expectedSeq - 1, record.Seq, "Audit chain mismatch.");
            if (usedLegacy) legacyRecords++;
            previous = record.Hash;
            expectedSeq++;
        }
        var message = legacyRecords == 0
            ? "Audit chain verified."
            : $"Audit chain verified; {legacyRecords} legacy PostgreSQL timestamp hash(es) accepted.";
        return new AuditVerificationDto(true, records.Count, null, message);
    }

    public async Task<AuditCheckpointDto> CheckpointAsync(Guid tenantId, string profile,
        CancellationToken cancellationToken)
    {
        if (profile is not ("standard" or "worm" or "anchored")) throw new InvalidOperationException("Invalid audit profile.");
        var records = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Seq).ToListAsync(cancellationToken);
        if (records.Count == 0) throw new InvalidOperationException("No audit records to checkpoint.");
        var hashes = records.Select(x => Encoding.UTF8.GetBytes(x.Hash)).ToArray();
        var root = MerkleRoot(hashes);
        var checkpoint = new AuditCheckpointRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ThroughSeq = records[^1].Seq,
            MerkleRoot = root, Profile = profile, CreatedAt = clock.GetUtcNow()
        };
        db.AuditCheckpoints.Add(checkpoint);
        await db.SaveChangesAsync(cancellationToken);
        if (profile is "worm" or "anchored")
            await anchors.StoreAsync(new AuditAnchorRecord(tenantId, checkpoint.ThroughSeq, root, checkpoint.CreatedAt), cancellationToken);
        return new AuditCheckpointDto(checkpoint.Id, checkpoint.ThroughSeq, root, profile, checkpoint.CreatedAt);
    }

    public async Task<AuditVerificationDto> VerifyAnchorsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var checkpoints = await db.AuditCheckpoints.AsNoTracking()
            .Where(x => x.TenantId == tenantId && (x.Profile == "worm" || x.Profile == "anchored"))
            .OrderBy(x => x.ThroughSeq).ToListAsync(cancellationToken);
        if (checkpoints.Count == 0) return new AuditVerificationDto(true, 0, null, "No external anchors recorded.");
        var checkedCount = 0;
        foreach (var checkpoint in checkpoints)
        {
            var anchor = await anchors.GetAsync(tenantId, checkpoint.ThroughSeq, cancellationToken);
            if (anchor is null || anchor.TenantId != tenantId)
                return new AuditVerificationDto(false, checkedCount, checkpoint.ThroughSeq, "Audit anchor is missing.");
            if (anchor.MerkleRoot != checkpoint.MerkleRoot)
                return new AuditVerificationDto(false, checkedCount, checkpoint.ThroughSeq, "Anchor checksum mismatch.");
            checkedCount++;
        }
        return new AuditVerificationDto(true, checkedCount, null, "Audit anchors verified.");
    }

    // PostgreSQL timestamptz stores microseconds, so hash the same precision that will be read back.
    private static DateTimeOffset RoundToPostgresPrecision(DateTimeOffset value)
    {
        var remainder = value.Ticks % TicksPerMicrosecond;
        var adjustment = remainder >= TicksPerMicrosecond / 2
            ? TicksPerMicrosecond - remainder
            : -remainder;
        return value.AddTicks(adjustment);
    }

    private static bool VerifyHash(AuditRecord record, string previousHash, bool allowLegacyPostgresPrecision,
        out bool usedLegacy)
    {
        usedLegacy = false;
        if (HashesEqual(record.Hash, ComputeHash(record, previousHash))) return true;
        if (!allowLegacyPostgresPrecision || record.CreatedAt.Ticks % TicksPerMicrosecond != 0) return false;

        // Pre-fix PostgreSQL rows hashed the original 100 ns ticks before timestamptz rounded them to microseconds.
        // Only the finite tick values inside PostgreSQL's rounding window can reproduce the stored hash.
        foreach (var offset in LegacyTimestampOffsets)
        {
            if (!HashesEqual(record.Hash, ComputeHash(record, previousHash, record.CreatedAt.AddTicks(offset)))) continue;
            usedLegacy = true;
            return true;
        }
        return false;
    }

    private static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string ComputeHash(AuditRecord record, string previousHash, DateTimeOffset? createdAt = null) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                tenant_id = record.TenantId, seq = record.Seq, event_type = record.EventType,
                actor = record.Actor, payload_json = record.PayloadJson,
                previous_hash = previousHash, created_at = createdAt ?? record.CreatedAt
            }, CanonicalOptions)))).ToLowerInvariant();

    private static string MerkleRoot(IReadOnlyList<byte[]> hashes)
    {
        if (hashes.Count == 0) return Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
        var level = hashes.ToList();
        while (level.Count > 1)
        {
            var next = new List<byte[]>();
            for (var i = 0; i < level.Count; i += 2)
            {
                var bytes = i + 1 < level.Count ? level[i].Concat(level[i + 1]).ToArray() : level[i];
                next.Add(SHA256.HashData(bytes));
            }
            level = next;
        }
        return Convert.ToHexString(level[0]).ToLowerInvariant();
    }
}
