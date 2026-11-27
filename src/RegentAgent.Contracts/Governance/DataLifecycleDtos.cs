using System.Text.Json.Serialization;

namespace RegentAgent.Contracts.Governance;

public sealed class DeletionRequestRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ResourceType { get; set; } = "Tenant";
    public string ResourceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset DeletedAt { get; set; }
    public DateTimeOffset RestoreUntil { get; set; }
    public DateTimeOffset? PurgeStartedAt { get; set; }
    public DateTimeOffset? PurgedAt { get; set; }
    public string Status { get; set; } = "SoftDeleted";
}

public sealed class DeletionProof
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset PurgedAt { get; set; }
    public string ManifestHash { get; set; } = string.Empty;
    public string ProofHash { get; set; } = string.Empty;
    public IReadOnlyList<Guid> LegalHoldIds { get; set; } = [];
    public IReadOnlyList<Guid> RemainingBackupIds { get; set; } = [];
}

public sealed class LegalHoldRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ResourceRef { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string AuthorizedBy { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class RetentionPolicyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string DataClass { get; set; } = "BusinessRecord";
    public int HotDays { get; set; } = 30;
    public int ArchiveDays { get; set; } = 180;
    public int ColdDays { get; set; } = 365;
    public int DeleteAfterDays { get; set; } = 730;
    public int Version { get; set; } = 1;
}

public sealed class ArchiveManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Period { get; set; } = string.Empty;
    public string Tier { get; set; } = "Archive";
    public string ObjectKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public string Status { get; set; } = "Archived";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BackupRunRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Profile { get; set; } = "local";
    public string ManifestObjectKey { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int RpoMinutes { get; set; } = 1440;
    public int RtoMinutes { get; set; } = 60;
    public string Status { get; set; } = "Completed";
    public bool RestoreVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RightsRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ArtifactRef { get; set; } = string.Empty;
    public string TenantOwnership { get; set; } = "TenantOwned";
    public string PlatformLicense { get; set; } = "None";
    public string ThirdPartyRights { get; set; } = "Unknown";
    public IReadOnlyList<string> SourceRefs { get; set; } = [];
    public string PublishStatus { get; set; } = "Blocked";
}

public sealed class LicenseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ArtifactRef { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string NoticeRef { get; set; } = string.Empty;
    public string SbomRef { get; set; } = string.Empty;
    public bool ContributorConfirmed { get; set; }
    public bool TrademarkReviewed { get; set; }
    public string PublishStatus { get; set; } = "Blocked";
}

public sealed class LifecycleEvaluation
{
    public IReadOnlyList<string> ArchiveCandidates { get; set; } = [];
    public IReadOnlyList<Guid> PurgeCandidates { get; set; } = [];
    public IReadOnlyList<Guid> LegalHoldBlocked { get; set; } = [];
}

public sealed class DataLifecycleState
{
    public Guid TenantId { get; set; }
    public List<DeletionRequestRecord> DeletionRequests { get; set; } = [];
    public List<DeletionProof> DeletionProofs { get; set; } = [];
    public List<LegalHoldRecord> LegalHolds { get; set; } = [];
    public List<RetentionPolicyRecord> RetentionPolicies { get; set; } = [];
    public List<ArchiveManifest> Archives { get; set; } = [];
    public List<BackupRunRecord> Backups { get; set; } = [];
    public List<RightsRecord> Rights { get; set; } = [];
    public List<LicenseRecord> Licenses { get; set; } = [];

    [JsonIgnore]
    public bool IsPurging => DeletionRequests.Any(request => request.Status == "Purging");

    [JsonIgnore]
    public bool IsPurged => DeletionProofs.Any(IsTenantPurgeProof);

    [JsonIgnore]
    public DateTimeOffset? PurgedAt => DeletionProofs.Where(IsTenantPurgeProof)
        .Select(proof => (DateTimeOffset?)proof.PurgedAt).Max();

    private bool IsTenantPurgeProof(DeletionProof proof) =>
        proof.ResourceType.Equals("Tenant", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParse(proof.ResourceId, out var tenantId) && tenantId == TenantId;
}
