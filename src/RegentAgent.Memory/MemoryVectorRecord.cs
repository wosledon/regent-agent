namespace RegentAgent.Memory;

public sealed record MemoryVectorRecord(
    string ClaimId,
    string TenantId,
    string ContentHash,
    string ModelVersion,
    float[] Vector,
    DateTimeOffset IndexedAt);
