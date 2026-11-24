namespace RegentAgent.Contracts.Identity;

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string CompanyName);
public sealed record LoginRequest(string Email, string Password, Guid? TenantId = null);
public sealed record AuthResult(string Email, string DisplayName, Guid UserId, Guid TenantId, Guid SessionId, string Role, IReadOnlyList<string> RecoveryCodes)
{
    public string AuthMethod { get; init; } = "Password";
    public string SessionKind { get; init; } = "Standard";
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
public sealed record RecoveryRequest(string Email, string Code, string NewPassword);
public sealed record SessionDto(Guid Id, Guid TenantId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Status,
    string AuthMethod = "Password", string SessionKind = "Standard");
public sealed record ExternalLoginRequest(Guid TenantId, string Issuer, string Subject, string Email, string DisplayName,
    string? Code = null, string? IdToken = null, string? CodeVerifier = null, string? Nonce = null);
public sealed record StartExternalLoginRequest(Guid TenantId, string? RedirectUri = null);
public sealed record ExternalAuthorizationDto(string AuthorizationUrl, string State, Guid TransactionId, DateTimeOffset ExpiresAt);
public sealed record ExternalCallbackRequest(string Code, string State);
public sealed record InvitationRequest(string Email, string Role, int ExpiresInHours);
public sealed record InvitationDto(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, string Status, string? Token);
public sealed record AcceptInvitationRequest(string Token);
public sealed record OwnershipDelegationDto(Guid Id, Guid DelegateUserId, DateTimeOffset ExpiresAt, string Status, string Reason);
public sealed record CreateOwnershipDelegationRequest(Guid DelegateUserId, int ExpiresInHours, string Reason);
public sealed record DisplaySuffixDto(string Suffix);
public sealed record IdentityProviderDto(Guid TenantId, string Provider, string Issuer, string ClientId, bool Enabled,
    string GroupMappingJson, string ClientSecretRef = "", string RedirectUri = "", string Scopes = "openid profile",
    string GroupClaim = "groups", string Status = "Active", int Version = 1);

public sealed record AuthenticationPolicyDto(
    string Mode,
    bool RequireGroupClaim,
    IReadOnlyList<string> AllowedAcrValues,
    IReadOnlyList<string> RequiredAmrValues,
    int MaxAuthenticationAgeMinutes,
    int MaxSessionMinutes,
    int IdleTimeoutMinutes,
    int Version);

public sealed record GroupMappingRefreshResult(int Evaluated, int Updated, int Denied, DateTimeOffset RefreshedAt);

public sealed record UpdateGroupMappingRequest(string GroupMappingJson);

public sealed record EmergencyAccessDto(Guid TenantId, string Credential, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record EmergencyAccessRequest(Guid TenantId, string Credential, string Purpose);

public sealed record EmergencyAccessResult(
    Guid UserId,
    Guid TenantId,
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    string Role,
    IReadOnlyList<string> Capabilities,
    string RotatedCredential)
{
    public string Email { get; init; } = string.Empty;
    public string SessionKind { get; init; } = "Emergency";
}
