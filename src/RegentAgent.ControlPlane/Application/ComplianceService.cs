using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed record ComplianceProfile(
    string TenantId,
    string Preset,
    IReadOnlyList<string> AllowedProviders,
    IReadOnlyList<string> AllowedRegions,
    int OnlineRetentionDays,
    string AuditProfile,
    bool BackupRequired,
    bool ExternalSharingAllowed,
    int Version,
    string Status);

public sealed class ComplianceService(JsonDocumentStore<ComplianceProfile> profiles)
{
    private const string SeedTenantId = "11111111-1111-1111-1111-111111111111";

    public Task SeedAsync(CancellationToken cancellationToken) => GetAsync(SeedTenantId, cancellationToken);

    public Task<ComplianceProfile> GetAsync(string tenantId, CancellationToken cancellationToken) =>
        profiles.GetOrAddAsync(tenantId, () => CreateBaseline(tenantId), cancellationToken);

    public Task<ComplianceProfile> UpdateAsync(string tenantId, ComplianceProfile update, CancellationToken cancellationToken) =>
        profiles.MutateAsync(tenantId, current =>
        {
            var baseline = current ?? CreateBaseline(tenantId);
            if (update.Version != baseline.Version)
                throw new VersionConflictException();
            return update with { TenantId = tenantId, Version = baseline.Version + 1, Status = "Active" };
        }, cancellationToken);

    private static ComplianceProfile CreateBaseline(string tenantId) => new(
        tenantId, "baseline", ["OpenAI", "Anthropic"], ["global"], 180, "standard", false, false, 1, "Active");
}

public sealed class VersionConflictException : Exception
{
    public const string Code = "VERSION_CONFLICT";

    public VersionConflictException() : base("The resource version is stale.")
    {
    }
}
