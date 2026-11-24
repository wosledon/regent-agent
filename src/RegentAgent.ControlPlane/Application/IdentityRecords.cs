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
    public string Status { get; set; } = "Active";
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
