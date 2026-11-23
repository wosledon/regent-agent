namespace RegentAgent.Contracts.Governance;

public sealed class ToolManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string SbomJson { get; set; } = "{}";
    public IReadOnlyList<string> Capabilities { get; set; } = [];
    public string Status { get; set; } = "Draft";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ToolAdmissionReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ToolId { get; set; }
    public string Reviewer { get; set; } = string.Empty;
    public bool SignatureValid { get; set; }
    public bool SbomValid { get; set; }
    public int CriticalVulnerabilities { get; set; }
    public string Verdict { get; set; } = "Pending";
    public IReadOnlyList<string> Findings { get; set; } = [];
}

public sealed record ToolAuthorizationRequest(string ToolName, string Version, string Actor, string Capability,
    string TaskGrant, string Taint, string PayloadJson);
public sealed record ToolAuthorizationResult(bool Allowed, string Reason, string? Digest);

public sealed class InjectionScanResult
{
    public bool Safe { get; set; }
    public string Taint { get; set; } = "Untrusted";
    public IReadOnlyList<string> Findings { get; set; } = [];
    public string RecommendedAction { get; set; } = "TreatAsData";
}

public sealed class DlpRedactionResult
{
    public string RedactedText { get; set; } = string.Empty;
    public IReadOnlyList<DlpToken> Tokens { get; set; } = [];
}
public sealed record DlpToken(string Token, string Classification);

public sealed class ContentSafetyFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string ContentRef { get; set; } = string.Empty;
    public string PolicyVersion { get; set; } = "baseline-v1";
    public string Category { get; set; } = string.Empty;
    public string Disposition { get; set; } = "Allow";
    public string Status { get; set; } = "Open";
    public string EvidenceRef { get; set; } = string.Empty;
    public string? AppealReason { get; set; }
    public string? AppealRequester { get; set; }
}
public sealed record ContentAppealRequest(Guid FindingId, string Reason, string Requester);
public sealed record ApprovalResolutionDto(string Approver);

public sealed class SecurityIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Severity { get; set; } = "SEV3";
    public string Title { get; set; } = string.Empty;
    public string Commander { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public IReadOnlyList<string> ContainmentActions { get; set; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; set; } = [];
    public IReadOnlyList<string> Notifications { get; set; } = [];
    public string? RecoverySummary { get; set; }
    public string? Retrospective { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ApprovalDelegation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string PrimaryApprover { get; set; } = string.Empty;
    public string DelegateApprover { get; set; } = string.Empty;
    public string ActionScope { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
    public string Reason { get; set; } = string.Empty;
}

public sealed class ToolUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ToolId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Capability { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed record ToolRevocationImpact(int AffectedActors, int AffectedCapabilities, IReadOnlyList<string> Actors);
public sealed record ApprovalChainDto(IReadOnlyList<string> Approvers);
public sealed record EvidenceVerificationDto(bool Valid, int CheckedEvidence, string? Message);

public sealed class GovernanceState
{
    public Guid TenantId { get; set; }
    public List<ToolManifest> Tools { get; set; } = [];
    public List<ToolAdmissionReview> AdmissionReviews { get; set; } = [];
    public List<ToolUsageRecord> ToolUsages { get; set; } = [];
    public List<DlpToken> DlpTokens { get; set; } = [];
    public List<ContentSafetyFinding> ContentFindings { get; set; } = [];
    public List<SecurityIncident> Incidents { get; set; } = [];
    public List<ApprovalDelegation> ApprovalDelegations { get; set; } = [];
}
