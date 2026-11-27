using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Identity;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class IdentityService
{
    private static readonly string[] ReservedGroupCapabilities =
        ["owner", "coowner", "tenant.owner", "ownership.manage", "models:write", "escalations:approve", "employee.terminate"];
    private static readonly SemaphoreSlim EmergencyAccessGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<AuthenticationPolicyDto> GetAuthenticationPolicyAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        return ToPolicyDto(await LoadPolicyAsync(tenantId, cancellationToken));
    }

    public async Task<AuthenticationPolicyDto> UpdateAuthenticationPolicyAsync(Guid userId, Guid tenantId,
        AuthenticationPolicyDto request, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        ValidatePolicy(request);
        var existing = await LoadPolicyAsync(tenantId, cancellationToken);
        if (request.Mode == "required") await EnsureRequiredSsoCanActivateAsync(tenantId, cancellationToken);
        var record = new AuthenticationPolicyRecord
        {
            TenantId = tenantId, Mode = request.Mode, RequireGroupClaim = request.RequireGroupClaim,
            AllowedAcrValues = request.AllowedAcrValues.Distinct(StringComparer.Ordinal).ToArray(),
            RequiredAmrValues = request.RequiredAmrValues.Distinct(StringComparer.Ordinal).ToArray(),
            MaxAuthenticationAgeMinutes = request.MaxAuthenticationAgeMinutes,
            MaxSessionMinutes = request.MaxSessionMinutes, IdleTimeoutMinutes = request.IdleTimeoutMinutes,
            Version = existing.Version + 1, UpdatedAt = clock.GetUtcNow()
        };
        await authenticationPolicies.PutAsync(tenantId.ToString(), record, cancellationToken);
        if (record.Mode == "required") await RevokeNonOidcSessionsAsync(tenantId, cancellationToken);
        await audit.AppendAsync(tenantId, "auth.policy.updated", "Owner", new
        {
            actor_type = "Human", user_id = userId, mode = record.Mode, version = record.Version,
            require_group_claim = record.RequireGroupClaim
        }, cancellationToken);
        return ToPolicyDto(record);
    }

    public async Task<IdentityProviderDto> GetIdentityProviderAsync(Guid userId, Guid tenantId, string provider,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        provider = NormalizeProvider(provider);
        var record = await providers.GetAsync($"{tenantId}:{provider}", cancellationToken)
            ?? throw new KeyNotFoundException("Identity provider not found.");
        return ToProviderDto(tenantId, record);
    }

    public async Task<IdentityProviderDto> ActivateProviderAsync(Guid userId, Guid tenantId, string provider,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        provider = NormalizeProvider(provider);
        var record = await providers.GetAsync($"{tenantId}:{provider}", cancellationToken)
            ?? throw new KeyNotFoundException("Identity provider not found.");
        await ValidateProviderActivationAsync(tenantId, record, cancellationToken);
        record.Enabled = true;
        record.Status = "Active";
        record.Version++;
        await providers.PutAsync($"{tenantId}:{provider}", record, cancellationToken);
        await audit.AppendAsync(tenantId, "auth.provider.activated", "Owner", new
        {
            actor_type = "Human", user_id = userId, provider, version = record.Version
        }, cancellationToken);
        return ToProviderDto(tenantId, record);
    }

    public async Task UpdateGroupMappingAsync(Guid userId, Guid tenantId, string groupMappingJson,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var mapping = ValidateGroupMapping(groupMappingJson);
        _ = mapping;
        var provider = await providers.GetAsync($"{tenantId}:Oidc", cancellationToken)
            ?? throw new KeyNotFoundException("OIDC provider not found.");
        provider.GroupMappingJson = JsonSerializer.Serialize(mapping, JsonOptions);
        provider.Version++;
        await providers.PutAsync($"{tenantId}:Oidc", provider, cancellationToken);
        await audit.AppendAsync(tenantId, "auth.group_mapping.updated", "Owner", new
        {
            actor_type = "Human", user_id = userId, provider = "Oidc", version = provider.Version,
            group_count = mapping.Count
        }, cancellationToken);
        await RefreshGroupMappingsCoreAsync(tenantId, cancellationToken);
    }

    public async Task<GroupMappingRefreshResult> RefreshGroupMappingsAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var result = await RefreshGroupMappingsCoreAsync(tenantId, cancellationToken);
        await audit.AppendAsync(tenantId, "auth.group_mapping.refreshed", "Owner", new
        {
            actor_type = "Human", user_id = userId, evaluated = result.Evaluated,
            updated = result.Updated, denied = result.Denied
        }, cancellationToken);
        return result;
    }

    public async Task<EmergencyAccessDto> ConfigureEmergencyAccessAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var credential = UrlToken();
        var now = clock.GetUtcNow();
        foreach (var active in (await emergencyCredentials.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.Status == "Active"))
        {
            active.Status = "Superseded";
            await emergencyCredentials.PutAsync(active.Id.ToString(), active, cancellationToken);
        }
        var record = new EmergencyAccessCredentialRecord
        {
            TenantId = tenantId, CodeHash = HashToken(credential), CreatedAt = now,
            ExpiresAt = now.AddDays(90), CreatedBy = userId, UserId = userId
        };
        await emergencyCredentials.PutAsync(record.Id.ToString(), record, cancellationToken);
        await audit.AppendAsync(tenantId, "auth.emergency_access.configured", "Owner", new
        {
            actor_type = "Human", user_id = userId, expires_at = record.ExpiresAt
        }, cancellationToken);
        return new EmergencyAccessDto(tenantId, credential, record.CreatedAt, record.ExpiresAt);
    }

    public async Task<EmergencyAccessResult> UseEmergencyAccessAsync(EmergencyAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Purpose)) throw new InvalidOperationException("Emergency access purpose is required.");
        if (!rateLimiter.TryAcquire($"emergency:{request.TenantId}")) throw new LoginRateExceededException();
        await EmergencyAccessGate.WaitAsync(cancellationToken);
        try
        {
            var codeHash = HashToken(request.Credential);
            var now = clock.GetUtcNow();
            var record = (await emergencyCredentials.GetAllAsync(cancellationToken)).SingleOrDefault(x =>
                x.TenantId == request.TenantId && x.Status == "Active" && x.ExpiresAt > now
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(x.CodeHash), Encoding.UTF8.GetBytes(codeHash)))
                ?? throw new UnauthorizedAccessException("Emergency access credential is invalid or expired.");
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == record.UserId, cancellationToken)
                ?? throw new UnauthorizedAccessException("Emergency access account is unavailable.");
            return await rlsScope.ExecuteAsync(request.TenantId, user.Id, async token =>
            {
                var membership = await db.Memberships.AsNoTracking()
                    .Where(x => x.TenantId == request.TenantId && x.UserId == user.Id)
                    .Select(x => new { x.Role, x.CapabilitiesJson }).SingleOrDefaultAsync(token)
                    ?? throw new UnauthorizedAccessException("Emergency access account is not a tenant member.");
                var role = membership.Role;
                if (role is not ("Owner" or "CoOwner")) throw new UnauthorizedAccessException("Emergency access requires an existing owner account.");
                var capabilities = JsonSerializer.Deserialize<string[]>(membership.CapabilitiesJson) ?? [];
                var authorityHash = MembershipAuthority.ComputeAuthorityHash(role, capabilities);
                record.Status = "Used";
                record.UsedAt = now;
                await emergencyCredentials.PutAsync(record.Id.ToString(), record, token);
                var rotatedCredential = UrlToken();
                var replacement = new EmergencyAccessCredentialRecord
                {
                    TenantId = request.TenantId, CodeHash = HashToken(rotatedCredential),
                    CreatedAt = now, ExpiresAt = now.AddDays(90), CreatedBy = record.CreatedBy, UserId = record.UserId
                };
                await emergencyCredentials.PutAsync(replacement.Id.ToString(), replacement, token);
                var session = new AuthSessionRecord
                {
                    UserId = user.Id, TenantId = request.TenantId, CreatedAt = now, LastActivityAt = now,
                    ExpiresAt = now.AddMinutes(15), AuthMethod = "Emergency", SessionKind = "Emergency",
                    AllowedActionsJson = JsonSerializer.Serialize(new[] { "sso.restore", "account.access" }, JsonOptions),
                    AuthorityHash = authorityHash,
                    IdleTimeoutMinutes = 15
                };
                await sessions.PutAsync(session.Id.ToString(), session, token);
                var owners = await db.Memberships.AsNoTracking().Where(x => x.TenantId == request.TenantId && x.Role == "Owner")
                    .Select(x => x.UserId).ToListAsync(token);
                foreach (var ownerId in owners)
                    db.Notifications.Add(new NotificationRecord
                    {
                        Id = Guid.NewGuid(), TenantId = request.TenantId, RecipientId = ownerId, Level = "Critical",
                        Title = "Emergency access was used", Status = "Available", CreatedAt = now
                    });
                await db.SaveChangesAsync(token);
                await audit.AppendAsync(request.TenantId, "auth.emergency_access.used", user.DisplayName, new
                {
                    actor_type = "Human", user_id = user.Id, session_id = session.Id,
                    expires_at = session.ExpiresAt, purpose = request.Purpose, credential_rotated = true
                }, token);
                return new EmergencyAccessResult(user.Id, request.TenantId, session.Id, session.ExpiresAt,
                    await DisplayNameAsync(user.Id, request.TenantId, user.DisplayName, token), role, capabilities,
                    rotatedCredential) { Email = user.Email };
            }, cancellationToken);
        }
        finally { EmergencyAccessGate.Release(); }
    }

    public async Task EnsureLocalLoginAllowedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        if (policy.Mode == "required") throw new UnauthorizedAccessException("This tenant requires SSO login.");
    }

    public async Task<IReadOnlyList<string>> ApplyExternalPolicyAsync(Guid tenantId, string provider,
        ExternalIdentityResult external, Guid userId, CancellationToken cancellationToken)
    {
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        var configuration = await providers.GetAsync($"{tenantId}:{provider}", cancellationToken)
            ?? throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        if (policy.Mode == "required" && provider != "Oidc")
            throw new UnauthorizedAccessException("This tenant requires OIDC SSO login.");
        if (policy.AllowedAcrValues.Length > 0 && (external.Acr is null || !policy.AllowedAcrValues.Contains(external.Acr, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("The identity provider did not return an accepted assurance level.");
        if (policy.RequiredAmrValues.Any(value => !external.Amr.Contains(value, StringComparer.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("The identity provider did not return the required authentication methods.");
        if (provider == "Oidc" && (external.AuthTime is not { } authTime
            || clock.GetUtcNow() - authTime > TimeSpan.FromMinutes(policy.MaxAuthenticationAgeMinutes)
            || authTime > clock.GetUtcNow().AddMinutes(1)))
            throw new UnauthorizedAccessException("The identity provider authentication is too old or missing auth_time.");
        if (policy.RequireGroupClaim && external.Groups.Count == 0)
            throw new UnauthorizedAccessException("The identity provider did not return the required group claim.");
        var capabilities = ComputeCapabilities(configuration.GroupMappingJson, external.Groups);
        var membership = await db.Memberships.SingleAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        membership.CapabilitiesJson = JsonSerializer.Serialize(capabilities, JsonOptions);
        await db.SaveChangesAsync(cancellationToken);
        await RevokeUserSessionsAsync(tenantId, userId, cancellationToken);
        return capabilities;
    }

    public async Task<IReadOnlyList<string>> LoadCapabilitiesAsync(Guid tenantId, Guid userId,
        CancellationToken cancellationToken)
    {
        var json = await db.Memberships.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => x.CapabilitiesJson)
            .SingleOrDefaultAsync(cancellationToken);
        return json is null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private async Task<AuthenticationPolicyRecord> LoadPolicyAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await authenticationPolicies.GetAsync(tenantId.ToString(), cancellationToken) ?? new AuthenticationPolicyRecord
        {
            TenantId = tenantId, UpdatedAt = clock.GetUtcNow()
        };

    private async Task EnsureRequiredSsoCanActivateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var provider = await providers.GetAsync($"{tenantId}:Oidc", cancellationToken);
        if (provider is null || !provider.Enabled || provider.Status != "Active"
            || !provider.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("An active HTTPS OIDC provider is required before enabling required SSO.");
        var credentials = await emergencyCredentials.GetAllAsync(cancellationToken);
        if (!credentials.Any(x => x.TenantId == tenantId && x.Status == "Active" && x.ExpiresAt > clock.GetUtcNow()))
            throw new InvalidOperationException("Emergency access must be configured before enabling required SSO.");
    }

    private async Task ValidateProviderActivationAsync(Guid tenantId, IdentityProviderRecord provider,
        CancellationToken cancellationToken)
    {
        ValidateGroupMapping(provider.GroupMappingJson);
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        if (provider.Provider == "Oidc")
        {
            if (!provider.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active OIDC issuers must use HTTPS.");
            if (policy.Mode == "required") await EnsureRequiredSsoCanActivateAsync(tenantId, cancellationToken);
        }
        else if (policy.Mode == "required")
            throw new InvalidOperationException("Required SSO tenants cannot activate GitHub login.");
    }

    private async Task RevokeNonOidcSessionsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var active = (await sessions.GetAllAsync(cancellationToken)).Where(x =>
            x.TenantId == tenantId && x.Status == "Active" && x.AuthMethod != "Oidc");
        await RevokeSessionsAsync(active, cancellationToken);
    }

    private async Task<GroupMappingRefreshResult> RefreshGroupMappingsCoreAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var provider = await providers.GetAsync($"{tenantId}:Oidc", cancellationToken)
            ?? throw new KeyNotFoundException("OIDC provider not found.");
        _ = ValidateGroupMapping(provider.GroupMappingJson);
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        var bindings = (await externalBindings.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.Provider == "Oidc").ToArray();
        var evaluated = 0;
        var updated = 0;
        var denied = 0;
        var changedUserIds = new HashSet<Guid>();
        foreach (var binding in bindings)
        {
            evaluated++;
            var groups = JsonSerializer.Deserialize<string[]>(binding.GroupsJson) ?? [];
            var membership = await db.Memberships.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == binding.UserId, cancellationToken);
            if (membership is null) continue;
            if (policy.RequireGroupClaim && groups.Length == 0)
            {
                membership.CapabilitiesJson = "[]";
                denied++;
                changedUserIds.Add(binding.UserId);
                continue;
            }
            var capabilities = ComputeCapabilities(provider.GroupMappingJson, groups);
            var serialized = JsonSerializer.Serialize(capabilities, JsonOptions);
            if (membership.CapabilitiesJson != serialized)
            {
                updated++;
                changedUserIds.Add(binding.UserId);
            }
            membership.CapabilitiesJson = serialized;
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var userId in changedUserIds)
            await RevokeUserSessionsAsync(tenantId, userId, cancellationToken);
        return new GroupMappingRefreshResult(evaluated, updated, denied, clock.GetUtcNow());
    }

    private static Dictionary<string, string[]> ValidateGroupMapping(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, JsonElement> root;
        try { root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? []; }
        catch (JsonException) { throw new InvalidOperationException("Group mapping must be a JSON object."); }
        var mapping = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, value) in root)
        {
            if (string.IsNullOrWhiteSpace(group)) throw new InvalidOperationException("Group mapping keys cannot be empty.");
            var capabilities = value.ValueKind switch
            {
                JsonValueKind.String => [value.GetString()!],
                JsonValueKind.Array => value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!).ToArray(),
                _ => throw new InvalidOperationException("Group mapping values must be a capability or capability array.")
            };
            if (capabilities.Length == 0 || capabilities.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Group mapping capabilities cannot be empty.");
            if (capabilities.Any(capability => ReservedGroupCapabilities.Contains(capability, StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Group mapping cannot grant owner or high-impact governance capabilities.");
            mapping[group] = capabilities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return mapping;
    }

    private static IReadOnlyList<string> ComputeCapabilities(string mappingJson, IReadOnlyList<string> groups)
    {
        var mapping = ValidateGroupMapping(mappingJson);
        return mapping.Where(pair => groups.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
    }

    private static void ValidatePolicy(AuthenticationPolicyDto policy)
    {
        if (policy.Mode is not ("optional" or "required")) throw new InvalidOperationException("Authentication mode must be optional or required.");
        if (policy.MaxSessionMinutes is < 5 or > 480 || policy.IdleTimeoutMinutes is < 1 or > 120
            || policy.MaxAuthenticationAgeMinutes is < 1 or > 1440)
            throw new InvalidOperationException("Authentication lifetime limits are invalid.");
        if (policy.AllowedAcrValues.Any(string.IsNullOrWhiteSpace) || policy.RequiredAmrValues.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("ACR and AMR policy values cannot be empty.");
    }

    private static AuthenticationPolicyDto ToPolicyDto(AuthenticationPolicyRecord record) => new(
        record.Mode, record.RequireGroupClaim, record.AllowedAcrValues, record.RequiredAmrValues,
        record.MaxAuthenticationAgeMinutes, record.MaxSessionMinutes, record.IdleTimeoutMinutes, record.Version);

    private static IdentityProviderDto ToProviderDto(Guid tenantId, IdentityProviderRecord record) => new(
        tenantId, record.Provider, record.Issuer, record.ClientId, record.Enabled, record.GroupMappingJson,
        record.ClientSecretRef, record.RedirectUri, record.Scopes, record.GroupClaim, record.Status, record.Version);
}
