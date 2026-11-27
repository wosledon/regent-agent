namespace RegentAgent.Memory;

internal static class PersonalMemoryAccess
{
    public static bool CanRead(MemoryClaim claim, string? viewer, string? viewerEmployeeId,
        string? viewerUserId, bool allowLegacyNameFallback, string? viewerEmploymentPeriodId = null)
    {
        if (claim.Scope != "personal") return true;
        if (!string.IsNullOrWhiteSpace(claim.AuthorEmploymentPeriodId))
        {
            return !string.IsNullOrWhiteSpace(viewerEmploymentPeriodId)
                && string.Equals(claim.AuthorEmploymentPeriodId, viewerEmploymentPeriodId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(claim.AuthorEmployeeId)
                    || string.Equals(claim.AuthorEmployeeId, viewerEmployeeId, StringComparison.Ordinal))
                && (string.IsNullOrWhiteSpace(claim.AuthorUserId)
                    || string.Equals(claim.AuthorUserId, viewerUserId, StringComparison.Ordinal));
        }
        if (!string.IsNullOrWhiteSpace(claim.AuthorEmployeeId))
            return !string.IsNullOrWhiteSpace(viewerEmployeeId)
                && string.Equals(claim.AuthorEmployeeId, viewerEmployeeId, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(claim.AuthorUserId))
            return !string.IsNullOrWhiteSpace(viewerUserId)
                && string.Equals(claim.AuthorUserId, viewerUserId, StringComparison.Ordinal);
        return allowLegacyNameFallback
            && !string.IsNullOrWhiteSpace(viewer)
            && string.Equals(claim.Author, viewer, StringComparison.Ordinal);
    }

    public static bool LegacyNameIsUnambiguous(IEnumerable<MemoryClaim> tenantClaims, string tenantId,
        string? viewer)
    {
        if (string.IsNullOrWhiteSpace(viewer)) return false;
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        var matching = tenantClaims.Where(x => MemoryVectorFormat.ValidateTenantId(x.TenantId) == tenant
            && x.Scope == "personal" && string.Equals(x.Author, viewer, StringComparison.Ordinal)).ToArray();
        return matching.Length == 1
            && string.IsNullOrWhiteSpace(matching[0].AuthorEmployeeId)
            && string.IsNullOrWhiteSpace(matching[0].AuthorUserId)
            && string.IsNullOrWhiteSpace(matching[0].AuthorEmploymentPeriodId);
    }

    public static bool MatchesOwner(MemoryClaim claim, string author, string? authorEmployeeId,
        string? authorUserId, bool allowLegacyNameFallback)
    {
        if (claim.Scope != "personal") return false;
        if (!string.IsNullOrWhiteSpace(claim.AuthorEmployeeId))
            return !string.IsNullOrWhiteSpace(authorEmployeeId)
                && string.Equals(claim.AuthorEmployeeId, authorEmployeeId, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(claim.AuthorUserId))
            return !string.IsNullOrWhiteSpace(authorUserId)
                && string.Equals(claim.AuthorUserId, authorUserId, StringComparison.Ordinal);
        return allowLegacyNameFallback
            && !string.IsNullOrWhiteSpace(author)
            && string.Equals(claim.Author, author, StringComparison.Ordinal);
    }
}
