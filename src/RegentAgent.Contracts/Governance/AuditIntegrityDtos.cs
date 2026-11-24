namespace RegentAgent.Contracts.Governance;

public sealed record AuditVerificationDto(bool Valid, long CheckedRecords, long? BrokenAtSeq, string? Message);
public sealed record AuditCheckpointDto(Guid Id, long ThroughSeq, string MerkleRoot, string Profile, DateTimeOffset CreatedAt);
public sealed record AuditAnchorRecord(Guid TenantId, long ThroughSeq, string MerkleRoot, DateTimeOffset CreatedAt);

public interface IAuditAnchorStore
{
    Task StoreAsync(AuditAnchorRecord anchor, CancellationToken cancellationToken);
    Task<AuditAnchorRecord?> GetAsync(Guid tenantId, long throughSeq, CancellationToken cancellationToken);
}
