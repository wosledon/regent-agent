namespace RegentAgent.Contracts.Identity;

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string CompanyName);
public sealed record LoginRequest(string Email, string Password, Guid? TenantId = null);
public sealed record AuthResult(string Email, string DisplayName, Guid UserId, Guid TenantId, Guid SessionId, string Role, IReadOnlyList<string> RecoveryCodes);
public sealed record RecoveryRequest(string Email, string Code, string NewPassword);
public sealed record SessionDto(Guid Id, Guid TenantId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Status);
public sealed record ExternalLoginRequest(Guid TenantId, string Issuer, string Subject, string Email, string DisplayName);
public sealed record InvitationRequest(string Email, string Role, int ExpiresInHours);
public sealed record InvitationDto(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt, string Status, string? Token);
public sealed record AcceptInvitationRequest(string Token);
public sealed record OwnershipDelegationDto(Guid Id, Guid DelegateUserId, DateTimeOffset ExpiresAt, string Status, string Reason);
public sealed record CreateOwnershipDelegationRequest(Guid DelegateUserId, int ExpiresInHours, string Reason);
public sealed record DisplaySuffixDto(string Suffix);
public sealed record IdentityProviderDto(Guid TenantId, string Provider, string Issuer, string ClientId, bool Enabled, string GroupMappingJson);
