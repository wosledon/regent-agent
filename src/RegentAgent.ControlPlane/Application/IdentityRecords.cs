namespace RegentAgent.ControlPlane.Application;

public sealed class CredentialRecord
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AuthSessionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string Status { get; set; } = "Active";
    public string AuthMethod { get; set; } = "Password";
    public string SessionKind { get; set; } = "Standard";
    public string AllowedActionsJson { get; set; } = "[]";
    public int IdleTimeoutMinutes { get; set; } = 30;
}

public sealed class RecoveryCodeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class IdentityProviderRecord
{
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretRef { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scopes { get; set; } = "openid profile";
    public string GroupClaim { get; set; } = "groups";
    public string Status { get; set; } = "Active";
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public string GroupMappingJson { get; set; } = "{}";
}

public sealed class InvitationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "Member";
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class ExternalIdentityBindingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public string ClaimsJson { get; set; } = "{}";
    public string GroupsJson { get; set; } = "[]";
    public string? Acr { get; set; }
    public string AmrJson { get; set; } = "[]";
    public DateTimeOffset? AuthTime { get; set; }
}

public sealed class ExternalLoginTransactionRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Nonce { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class AuthenticationPolicyRecord
{
    public Guid TenantId { get; set; }
    public string Mode { get; set; } = "optional";
    public bool RequireGroupClaim { get; set; }
    public string[] AllowedAcrValues { get; set; } = [];
    public string[] RequiredAmrValues { get; set; } = [];
    public int MaxAuthenticationAgeMinutes { get; set; } = 60;
    public int MaxSessionMinutes { get; set; } = 480;
    public int IdleTimeoutMinutes { get; set; } = 30;
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class EmergencyAccessCredentialRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid UserId { get; set; }
}

public sealed class UserTenantProfileRecord
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string DisplaySuffix { get; set; } = string.Empty;
}

public sealed class OwnershipDelegationRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PrimaryUserId { get; set; }
    public Guid DelegateUserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Status { get; set; } = "Active";
    public string Reason { get; set; } = string.Empty;
}
