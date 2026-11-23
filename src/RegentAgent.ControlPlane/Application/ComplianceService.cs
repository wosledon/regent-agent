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
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        const string tenantId = "11111111-1111-1111-1111-111111111111";
        if (await profiles.GetAsync(tenantId, cancellationToken) is not null) return;
        await profiles.PutAsync(tenantId, new ComplianceProfile(tenantId, "baseline",
            ["OpenAI", "Anthropic"], ["global"], 180, "standard", false, false, 1, "Active"), cancellationToken);
    }

    public Task<ComplianceProfile?> GetAsync(string tenantId, CancellationToken cancellationToken) => profiles.GetAsync(tenantId, cancellationToken);

    public async Task<ComplianceProfile> UpdateAsync(string tenantId, ComplianceProfile update, CancellationToken cancellationToken)
    {
        var current = await profiles.GetAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException("Compliance profile not found.");
        var next = update with { TenantId = tenantId, Version = current.Version + 1, Status = "Active" };
        await profiles.PutAsync(tenantId, next, cancellationToken);
        return next;
    }
}
