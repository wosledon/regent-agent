using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Identity;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class IdentityService
{
    public async Task<InvitationDto> CreateInvitationAsync(Guid userId, Guid tenantId,
        InvitationRequest request, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        if (request.Role is not ("Member" or "CoOwner")) throw new InvalidOperationException("Invalid invitation role.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var record = new InvitationRecord
        {
            TenantId = tenantId, Email = request.Email.Trim().ToLowerInvariant(), Role = request.Role,
            TokenHash = HashToken(token), ExpiresAt = clock.GetUtcNow().AddHours(request.ExpiresInHours)
        };
        await invitations.PutAsync(record.Id.ToString(), record, cancellationToken);
        return new InvitationDto(record.Id, record.Email, record.Role, record.ExpiresAt, record.Status, token);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListInvitationsAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        return (await invitations.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .Select(x => new InvitationDto(x.Id, x.Email, x.Role, x.ExpiresAt, x.Status, null)).ToArray();
    }

    public async Task AcceptInvitationAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(token);
        var record = (await invitations.GetAllAsync(cancellationToken))
            .FirstOrDefault(x => x.TokenHash == tokenHash && x.Status == "Pending" && x.ExpiresAt > clock.GetUtcNow())
            ?? throw new UnauthorizedAccessException("Invitation is invalid or expired.");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");
        if (!string.Equals(user.Email, record.Email, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invitation email does not match the signed-in account.");
        if (!await db.Memberships.AnyAsync(x => x.TenantId == record.TenantId && x.UserId == userId, cancellationToken))
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = record.TenantId, UserId = userId, Role = record.Role
            });
        record.Status = "Accepted";
        await invitations.PutAsync(record.Id.ToString(), record, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<OwnershipDelegationDto> CreateOwnershipDelegationAsync(Guid userId, Guid tenantId,
        CreateOwnershipDelegationRequest request, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        if (!await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.UserId == request.DelegateUserId, cancellationToken))
            throw new KeyNotFoundException("Delegate is not a tenant member.");
        var record = new OwnershipDelegationRecord
        {
            TenantId = tenantId, PrimaryUserId = userId, DelegateUserId = request.DelegateUserId,
            ExpiresAt = clock.GetUtcNow().AddHours(request.ExpiresInHours), Reason = request.Reason
        };
        await ownershipDelegations.PutAsync(record.Id.ToString(), record, cancellationToken);
        return new OwnershipDelegationDto(record.Id, record.DelegateUserId, record.ExpiresAt, record.Status, record.Reason);
    }

    public async Task<IReadOnlyList<OwnershipDelegationDto>> ListOwnershipDelegationsAsync(Guid userId,
        Guid tenantId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        return (await ownershipDelegations.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .Select(x => new OwnershipDelegationDto(x.Id, x.DelegateUserId, x.ExpiresAt, x.Status, x.Reason)).ToArray();
    }

    public async Task RevokeOwnershipDelegationAsync(Guid userId, Guid tenantId, Guid delegationId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var record = await ownershipDelegations.GetAsync(delegationId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException("Delegation not found.");
        if (record.TenantId != tenantId) throw new UnauthorizedAccessException("Delegation belongs to another tenant.");
        record.Status = "Revoked";
        await ownershipDelegations.PutAsync(record.Id.ToString(), record, cancellationToken);
    }

    public async Task<IdentityProviderDto> ConfigureProviderAsync(Guid userId, Guid tenantId,
        IdentityProviderDto provider, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        if (provider.Provider is not ("Oidc" or "GitHub")) throw new InvalidOperationException("Unsupported identity provider.");
        if (provider.Provider == "Oidc" && !string.IsNullOrWhiteSpace(provider.Issuer)
            && !provider.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && provider.Status == "Active")
            throw new InvalidOperationException("Active OIDC issuers must use HTTPS.");
        var existing = await providers.GetAsync($"{tenantId}:{provider.Provider}", cancellationToken);
        var record = new IdentityProviderRecord
        {
            TenantId = tenantId, Provider = provider.Provider, Issuer = provider.Issuer,
            ClientId = provider.ClientId, ClientSecretRef = provider.ClientSecretRef,
            RedirectUri = provider.RedirectUri, Scopes = provider.Scopes, GroupClaim = provider.GroupClaim,
            Status = provider.Status, Version = (existing?.Version ?? 0) + 1,
            Enabled = provider.Enabled, GroupMappingJson = provider.GroupMappingJson
        };
        await providers.PutAsync($"{tenantId}:{provider.Provider}", record, cancellationToken);
        return new IdentityProviderDto(tenantId, record.Provider, record.Issuer, record.ClientId, record.Enabled,
            record.GroupMappingJson, record.ClientSecretRef, record.RedirectUri, record.Scopes,
            record.GroupClaim, record.Status, record.Version);
    }

    private async Task RequireOwnerAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = await db.Memberships.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is not ("Owner" or "CoOwner")) throw new UnauthorizedAccessException("Owner permission required.");
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
