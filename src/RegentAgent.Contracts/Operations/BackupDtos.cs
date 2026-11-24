namespace RegentAgent.Contracts.Operations;

public sealed record BackupProfileRecord(string Id, string DeploymentId, bool Enabled, string Components, int RetentionDays)
{
    public bool PostgresFull { get; init; }
    public bool PostgresPitr { get; init; }
    public bool RustFs { get; init; }
    public bool KeyVault { get; init; }
    public bool Offsite { get; init; }
    public bool RestoreDrills { get; init; }
    public string Schedule { get; init; } = "daily";
    public string TargetRef { get; init; } = string.Empty;
    public int Version { get; init; } = 1;
}

public sealed class BackupJobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "default";
    public int ProfileVersion { get; set; }
    public string Component { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string SnapshotRef { get; set; } = string.Empty;
    public string ManifestRef { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset? RetentionUntil { get; set; }
    public string? Error { get; set; }
}

public sealed class RestoreJobRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = "default";
    public IReadOnlyList<Guid> BackupIds { get; set; } = [];
    public string Scope { get; set; } = "Full";
    public string ApprovalRef { get; set; } = string.Empty;
    public string Requester { get; set; } = string.Empty;
    public string? Approver { get; set; }
    public string Status { get; set; } = "PendingApproval";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public int? RpoActualMinutes { get; set; }
    public int? RtoActualMinutes { get; set; }
    public string ReportRef { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed record BackupCoverageDto(
    string ProfileId,
    bool Enabled,
    string Status,
    IReadOnlyList<string> EnabledComponents,
    DateTimeOffset? LastSuccessfulAt,
    int? EffectiveRpoMinutes,
    int? EffectiveRtoMinutes,
    string RpoStatus,
    string RtoStatus);

public sealed record BackupRunRequest(string ProfileId = "default");
public sealed record RestoreRequest(IReadOnlyList<Guid> BackupIds, string Scope, string ApprovalRef, string Requester);
public sealed record RestoreApprovalRequest(string Approver, string Resolution);
