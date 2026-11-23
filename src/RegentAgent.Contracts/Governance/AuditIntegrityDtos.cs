namespace RegentAgent.Contracts.Governance;

public sealed record AuditVerificationDto(bool Valid, long CheckedRecords, long? BrokenAtSeq, string? Message);
public sealed record AuditCheckpointDto(Guid Id, long ThroughSeq, string MerkleRoot, string Profile, DateTimeOffset CreatedAt);
