namespace RegentAgent.Memory;

public static class MemoryVectorFormat
{
    public const int Dimensions = 256;
    public const int MaximumSearchResults = 200;

    public static Guid ValidateTenantId(string? tenantId)
    {
        if (!Guid.TryParse(tenantId, out var tenant) || tenant == Guid.Empty)
            throw new ArgumentException("Tenant ID is invalid.", nameof(tenantId));
        return tenant;
    }

    public static string ValidateClaimId(string? claimId)
    {
        if (string.IsNullOrWhiteSpace(claimId))
            throw new ArgumentException("Claim ID is required.", nameof(claimId));
        return claimId;
    }

    public static Guid ValidateRecord(MemoryVectorRecord? record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _ = ValidateClaimId(record.ClaimId);
        var tenant = ValidateTenantId(record.TenantId);
        if (string.IsNullOrWhiteSpace(record.ContentHash))
            throw new ArgumentException("Content hash is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.ModelVersion))
            throw new ArgumentException("Model version is required.", nameof(record));
        ValidateVector(record.Vector);
        return tenant;
    }

    public static void ValidateSearch(float[]? queryVector, IReadOnlyList<string>? allowedClaimIds, int maxResults)
    {
        ValidateVector(queryVector);
        ArgumentNullException.ThrowIfNull(allowedClaimIds);
        if (maxResults is < 1 or > MaximumSearchResults)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        if (allowedClaimIds.Count > MaximumSearchResults)
            throw new ArgumentException("Allowed claim set is too large.", nameof(allowedClaimIds));
        if (allowedClaimIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Allowed claim IDs are invalid.", nameof(allowedClaimIds));
    }

    public static void ValidateVector(float[]? vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length != Dimensions || vector.Any(value => !float.IsFinite(value)))
            throw new ArgumentException($"Vector must contain {Dimensions} finite values.", nameof(vector));
    }
}

public sealed record MemoryVectorRecord(
    string ClaimId,
    string TenantId,
    string ContentHash,
    string ModelVersion,
    float[] Vector,
    DateTimeOffset IndexedAt);
