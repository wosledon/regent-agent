namespace RegentAgent.Storage;

public static class TenantObjectNamespace
{
    public static string CanonicalPrefix(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant id is invalid.", nameof(tenantId));
        return $"tenants/{tenantId:D}/";
    }

    public static IReadOnlyList<string> AllPrefixes(Guid tenantId)
    {
        var canonical = CanonicalPrefix(tenantId);
        var compact = $"tenants/{tenantId:N}/";
        return string.Equals(canonical, compact, StringComparison.Ordinal) ? [canonical] : [canonical, compact];
    }

    public static bool Contains(string objectKey, Guid tenantId) =>
        AllPrefixes(tenantId).Any(prefix => objectKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool TryGetTenantId(string objectKey, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        const string tenantPrefix = "tenants/";
        if (!objectKey.StartsWith(tenantPrefix, StringComparison.OrdinalIgnoreCase)) return false;
        var slash = objectKey.IndexOf('/', tenantPrefix.Length);
        return slash > tenantPrefix.Length
            && Guid.TryParse(objectKey[tenantPrefix.Length..slash], out tenantId);
    }
}
