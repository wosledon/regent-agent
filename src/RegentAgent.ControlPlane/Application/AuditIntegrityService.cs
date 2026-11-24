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
                else if (record.PreviousHash != previous || record.Hash != ComputeHash(record, previous))
                {
                    break;
                }
                previous = record.Hash;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuditRecord> AppendAsync(Guid tenantId, string eventType, string actor, object payload,
        CancellationToken cancellationToken)
    {
        var previous = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Seq).FirstOrDefaultAsync(cancellationToken);
        var record = new AuditRecord
        {
            TenantId = tenantId, Seq = (previous?.Seq ?? 0) + 1,
            EventType = eventType, Actor = actor,
            PayloadJson = JsonSerializer.Serialize(payload, CanonicalOptions),
            CreatedAt = clock.GetUtcNow(), PreviousHash = previous?.Hash ?? string.Empty
        };
        record.Hash = ComputeHash(record, record.PreviousHash);
        db.AuditRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<AuditVerificationDto> VerifyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var records = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Seq).ToListAsync(cancellationToken);
        string previous = string.Empty;
        long expectedSeq = 1;
        foreach (var record in records)
        {
            if (record.Seq != expectedSeq || record.PreviousHash != previous ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(record.Hash), Encoding.UTF8.GetBytes(ComputeHash(record, previous))))
                return new AuditVerificationDto(false, expectedSeq - 1, record.Seq, "Audit chain mismatch.");
            previous = record.Hash;
            expectedSeq++;
        }
        return new AuditVerificationDto(true, records.Count, null, "Audit chain verified.");
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

    private static string ComputeHash(AuditRecord record, string previousHash) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new
        {
            tenant_id = record.TenantId, seq = record.Seq, event_type = record.EventType,
            actor = record.Actor, payload_json = record.PayloadJson,
            previous_hash = previousHash, created_at = record.CreatedAt
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
