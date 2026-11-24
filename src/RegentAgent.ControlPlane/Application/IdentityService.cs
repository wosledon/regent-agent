using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Identity;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class IdentityService(
    RegentDbContext db,
    TimeProvider clock,
    LoginRateLimiter rateLimiter,
    JsonDocumentStore<CredentialRecord> credentials,
    JsonDocumentStore<AuthSessionRecord> sessions,
    JsonDocumentStore<RecoveryCodeRecord> recoveryCodes,
    JsonDocumentStore<UserTenantProfileRecord> profiles,
    JsonDocumentStore<IdentityProviderRecord> providers,
    JsonDocumentStore<InvitationRecord> invitations,
    JsonDocumentStore<OwnershipDelegationRecord> ownershipDelegations,
    JsonDocumentStore<ExternalIdentityBindingRecord> externalBindings,
    JsonDocumentStore<ExternalLoginTransactionRecord> externalTransactions,
    JsonDocumentStore<AuthenticationPolicyRecord> authenticationPolicies,
    JsonDocumentStore<EmergencyAccessCredentialRecord> emergencyCredentials,
    AuditIntegrityService audit,
    IExternalIdentityProviderResolver? externalProviders = null)
{
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.CompanyName))
            throw new InvalidOperationException("Display name and company name are required.");
        if (request.Password.Length < 10) throw new InvalidOperationException("Password must contain at least 10 characters.");
        if (await db.Users.AnyAsync(x => x.Email == email, cancellationToken))
            throw new InvalidOperationException("Email is already registered.");
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        db.Users.Add(new UserRecord { Id = userId, Email = email, DisplayName = request.DisplayName });
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = email.Split('@')[0], CompanyName = request.CompanyName });
        db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = "Owner" });
        db.Charters.Add(new CharterRecord
        {
            TenantId = tenantId, Mission = "Build useful products autonomously.", MonthlyBudgetMinor = 1_000_000,
            Currency = "CNY", Version = 1, UpdatedAt = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(cancellationToken);
        await credentials.PutAsync(userId.ToString(), new CredentialRecord
        {
            UserId = userId, PasswordHash = HashPassword(request.Password), UpdatedAt = clock.GetUtcNow()
        }, cancellationToken);
        var codes = await IssueRecoveryCodesAsync(userId, cancellationToken);
        var session = await CreateSessionAsync(userId, tenantId, cancellationToken);
        var displayName = await DisplayNameAsync(userId, tenantId, request.DisplayName, cancellationToken);
        return new AuthResult(email, displayName, userId, tenantId, session.Id, "Owner", codes);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!rateLimiter.TryAcquire($"login:{email}")) throw new LoginRateExceededException();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, cancellationToken)
            ?? throw new UnauthorizedAccessException("Invalid email or password.");
        var credential = await credentials.GetAsync(user.Id.ToString(), cancellationToken);
        if (credential is null || !VerifyPassword(request.Password, credential.PasswordHash))
            throw new UnauthorizedAccessException("Invalid email or password.");
        if (PasswordHasher.IsLegacy(credential.PasswordHash))
        {
            credential.PasswordHash = HashPassword(request.Password);
            credential.UpdatedAt = clock.GetUtcNow();
            await credentials.PutAsync(user.Id.ToString(), credential, cancellationToken);
        }
        var memberships = await db.Memberships.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
        var membership = memberships.FirstOrDefault(x => request.TenantId is null || x.TenantId == request.TenantId)
            ?? memberships.FirstOrDefault() ?? throw new UnauthorizedAccessException("No tenant membership.");
        await EnsureLocalLoginAllowedAsync(membership.TenantId, cancellationToken);
        var session = await CreateSessionAsync(user.Id, membership.TenantId, cancellationToken);
        var displayName = await DisplayNameAsync(user.Id, membership.TenantId, user.DisplayName, cancellationToken);
        return new AuthResult(user.Email, displayName, user.Id, membership.TenantId, session.Id, membership.Role, []);
    }

    public async Task<ExternalAuthorizationDto> StartExternalLoginAsync(Guid tenantId, string provider,
        string? redirectUri, CancellationToken cancellationToken)
    {
        provider = NormalizeProvider(provider);
        var identity = await providers.GetAsync($"{tenantId}:{provider}", cancellationToken)
            ?? throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        if (!identity.Enabled) throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        if (policy.Mode == "required" && provider != "Oidc")
            throw new UnauthorizedAccessException("This tenant requires OIDC SSO login.");
        redirectUri = string.IsNullOrWhiteSpace(redirectUri) ? identity.RedirectUri : redirectUri.Trim();
        if (!string.Equals(redirectUri, identity.RedirectUri, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Redirect URI does not match the tenant identity provider.");
        var configuration = ProviderConfiguration(identity);
        var resolver = IdentityResolver();
        var state = UrlToken();
        var nonce = UrlToken();
        var verifier = UrlToken();
        var challenge = Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        var authorizationUrl = await resolver.Resolve(provider).BuildAuthorizationUrlAsync(
            new ExternalAuthorizationParameters(state, nonce, challenge, redirectUri,
                policy.AllowedAcrValues.Length > 0 ? string.Join(' ', policy.AllowedAcrValues) : null,
                policy.MaxAuthenticationAgeMinutes * 60,
                policy.Mode == "required" ? "login" : null), configuration, cancellationToken);
        var transaction = new ExternalLoginTransactionRecord
        {
            TenantId = tenantId, Provider = provider, State = state, Nonce = nonce,
            CodeVerifier = verifier, CodeChallenge = challenge, RedirectUri = redirectUri,
            CreatedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddMinutes(10)
        };
        await externalTransactions.PutAsync(transaction.Id.ToString(), transaction, cancellationToken);
        return new ExternalAuthorizationDto(authorizationUrl, state, transaction.Id, transaction.ExpiresAt);
    }

    public async Task<AuthResult> CompleteExternalLoginAsync(string provider, Guid transactionId,
        ExternalCallbackRequest callback, CancellationToken cancellationToken)
    {
        provider = NormalizeProvider(provider);
        var transaction = await externalTransactions.GetAsync(transactionId.ToString(), cancellationToken)
            ?? throw new UnauthorizedAccessException("External login transaction is invalid.");
        if (transaction.Provider != provider || transaction.Status != "Pending" || transaction.ExpiresAt <= clock.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(transaction.State), Encoding.UTF8.GetBytes(callback.State)))
            throw new UnauthorizedAccessException("External login transaction is invalid or expired.");
        transaction.Status = "Consumed";
        await externalTransactions.PutAsync(transaction.Id.ToString(), transaction, cancellationToken);
        var identity = await providers.GetAsync($"{transaction.TenantId}:{provider}", cancellationToken)
            ?? throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        if (!identity.Enabled) throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        var external = await IdentityResolver().Resolve(provider).AuthenticateAsync(new ExternalIdentityRequest(
            transaction.TenantId, provider, identity.Issuer, null, null, null, callback.Code,
            CodeVerifier: transaction.CodeVerifier, Nonce: transaction.Nonce), ProviderConfiguration(identity), cancellationToken);
        var user = await ResolveExternalUserAsync(transaction.TenantId, provider, external, false, cancellationToken);
        var capabilities = await ApplyExternalPolicyAsync(transaction.TenantId, provider, external, user.Id, cancellationToken);
        var session = await CreateSessionAsync(user.Id, transaction.TenantId, cancellationToken,
            TimeSpan.FromHours(8), provider);
        var displayName = await DisplayNameAsync(user.Id, transaction.TenantId, user.DisplayName, cancellationToken);
        var role = await db.Memberships.AsNoTracking().Where(x => x.TenantId == transaction.TenantId && x.UserId == user.Id)
            .Select(x => x.Role).SingleAsync(cancellationToken);
        return new AuthResult(user.Email, displayName, user.Id, transaction.TenantId, session.Id, role, [])
        {
            AuthMethod = provider, Capabilities = capabilities
        };
    }

    public async Task<AuthResult> ExternalLoginAsync(ExternalLoginRequest request, string provider,
        CancellationToken cancellationToken)
    {
        provider = NormalizeProvider(provider);
        var identity = await providers.GetAsync($"{request.TenantId}:{provider}", cancellationToken)
            ?? throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        if (!identity.Enabled || !string.Equals(identity.Issuer, request.Issuer, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        var adapter = IdentityResolver().Resolve(provider);
        if (!adapter.Provider.Equals("local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Remote identity login requires the authorization transaction endpoints.");
        var policy = await LoadPolicyAsync(request.TenantId, cancellationToken);
        if (policy.Mode == "required" && provider != "Oidc")
            throw new UnauthorizedAccessException("This tenant requires OIDC SSO login.");
        var external = await adapter.AuthenticateAsync(new ExternalIdentityRequest(
            request.TenantId, provider, request.Issuer, request.Subject, request.Email, request.DisplayName,
            request.Code, request.IdToken, request.CodeVerifier, request.Nonce), ProviderConfiguration(identity), cancellationToken);
        var user = await ResolveExternalUserAsync(request.TenantId, provider, external,
            policy.Mode != "required", cancellationToken);
        var capabilities = await ApplyExternalPolicyAsync(request.TenantId, provider, external, user.Id, cancellationToken);
        var session = await CreateSessionAsync(user.Id, request.TenantId, cancellationToken,
            TimeSpan.FromHours(8), provider);
        var displayName = await DisplayNameAsync(user.Id, request.TenantId, user.DisplayName, cancellationToken);
        var role = await db.Memberships.AsNoTracking().Where(x => x.TenantId == request.TenantId && x.UserId == user.Id)
            .Select(x => x.Role).SingleAsync(cancellationToken);
        return new AuthResult(user.Email, displayName, user.Id, request.TenantId, session.Id, role, [])
        {
            AuthMethod = provider, Capabilities = capabilities
        };
    }

    private async Task<UserRecord> ResolveExternalUserAsync(Guid tenantId, string provider,
        ExternalIdentityResult external, bool allowLocalJit, CancellationToken cancellationToken)
    {
        if (!external.EmailVerified || string.IsNullOrWhiteSpace(external.Subject)
            || string.IsNullOrWhiteSpace(external.Email))
            throw new UnauthorizedAccessException("Verified issuer, subject and email are required.");
        var bindings = await externalBindings.GetAllAsync(cancellationToken);
        var binding = bindings.SingleOrDefault(x => x.TenantId == tenantId && x.Provider == provider
            && x.Issuer == external.Issuer && x.Subject == external.Subject);
        if (binding is not null)
        {
            var boundUser = await db.Users.SingleOrDefaultAsync(x => x.Id == binding.UserId, cancellationToken)
                ?? throw new UnauthorizedAccessException("External identity binding is invalid.");
            if (!await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.UserId == boundUser.Id, cancellationToken))
                throw new UnauthorizedAccessException("External identity is not a member of this tenant.");
            await UpdateExternalBindingMetadataAsync(binding, external, cancellationToken);
            return boundUser;
        }
        var email = external.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        var invitation = (await invitations.GetAllAsync(cancellationToken)).SingleOrDefault(x =>
            x.TenantId == tenantId && x.Email == email && x.Status == "Pending" && x.ExpiresAt > clock.GetUtcNow());
        var isMember = user is not null && await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id, cancellationToken);
        if (user is null)
        {
            if (invitation is null && !allowLocalJit)
                throw new UnauthorizedAccessException("A valid tenant invitation is required for external login.");
            user = new UserRecord { Id = Guid.NewGuid(), Email = email, DisplayName = external.DisplayName };
            db.Users.Add(user);
        }
        if (invitation is not null)
        {
            if (!await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.UserId == user.Id, cancellationToken))
                db.Memberships.Add(new MembershipRecord
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, Role = invitation.Role
                });
            invitation.Status = "Accepted";
            await invitations.PutAsync(invitation.Id.ToString(), invitation, cancellationToken);
        }
        else if (!isMember)
        {
            if (!allowLocalJit)
                throw new UnauthorizedAccessException("A valid tenant invitation is required for external login.");
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, Role = "Member" });
        }
        var newBinding = new ExternalIdentityBindingRecord
        {
            TenantId = tenantId, Provider = provider, Issuer = external.Issuer, Subject = external.Subject,
            UserId = user.Id, LinkedAt = clock.GetUtcNow()
        };
        await UpdateExternalBindingMetadataAsync(newBinding, external, cancellationToken);
        await externalBindings.PutAsync(newBinding.Id.ToString(), newBinding, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    private async Task UpdateExternalBindingMetadataAsync(ExternalIdentityBindingRecord binding,
        ExternalIdentityResult external, CancellationToken cancellationToken)
    {
        binding.ClaimsJson = JsonSerializer.Serialize(external.Claims);
        binding.GroupsJson = JsonSerializer.Serialize(external.Groups);
        binding.Acr = external.Acr;
        binding.AmrJson = JsonSerializer.Serialize(external.Amr);
        binding.AuthTime = external.AuthTime;
        await externalBindings.PutAsync(binding.Id.ToString(), binding, cancellationToken);
    }

    private IExternalIdentityProviderResolver IdentityResolver() => externalProviders
        ?? new ExternalIdentityProviderResolver([new LocalExternalIdentityProvider()], new ExternalIdentityOptions());

    private static ExternalIdentityProviderConfiguration ProviderConfiguration(IdentityProviderRecord identity) =>
        new(identity.Issuer, identity.ClientId, identity.ClientSecretRef, identity.RedirectUri, identity.Scopes, identity.GroupClaim);

    private static string NormalizeProvider(string provider) => provider.ToLowerInvariant() switch
    {
        "github" => "GitHub",
        "oidc" => "Oidc",
        _ => throw new InvalidOperationException("Unsupported identity provider.")
    };

    private static string UrlToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task RecoverAsync(RecoveryRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!rateLimiter.TryAcquire($"recovery:{email}")) throw new LoginRateExceededException();
        if (request.NewPassword.Length < 10) throw new InvalidOperationException("Password must contain at least 10 characters.");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email, cancellationToken)
            ?? throw new UnauthorizedAccessException("Invalid recovery code.");
        var codeHash = HashRecoveryCode(request.Code);
        var codes = await recoveryCodes.GetAllAsync(cancellationToken);
        var code = codes.FirstOrDefault(x => x.UserId == user.Id && x.CodeHash == codeHash && x.UsedAt is null)
            ?? throw new UnauthorizedAccessException("Invalid recovery code.");
        code.UsedAt = clock.GetUtcNow();
        await recoveryCodes.PutAsync(code.Id.ToString(), code, cancellationToken);
        await credentials.PutAsync(user.Id.ToString(), new CredentialRecord
        {
            UserId = user.Id, PasswordHash = HashPassword(request.NewPassword), UpdatedAt = clock.GetUtcNow()
        }, cancellationToken);
        var active = (await sessions.GetAllAsync(cancellationToken)).Where(x => x.UserId == user.Id && x.Status == "Active").ToArray();
        foreach (var session in active)
        {
            session.Status = "Revoked";
            await sessions.PutAsync(session.Id.ToString(), session, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await sessions.GetAllAsync(cancellationToken)).Where(x => x.UserId == userId)
            .Select(x => new SessionDto(x.Id, x.TenantId, x.CreatedAt, x.ExpiresAt, x.Status,
                x.AuthMethod, x.SessionKind)).ToArray();

    public async Task RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var session = await sessions.GetAsync(sessionId.ToString(), cancellationToken);
        if (session is null || session.UserId != userId) throw new KeyNotFoundException("Session not found.");
        session.Status = "Revoked";
        await sessions.PutAsync(session.Id.ToString(), session, cancellationToken);
    }

    public async Task<string> GetDisplaySuffixAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        (await profiles.GetAsync($"{userId}:{tenantId}", cancellationToken))?.DisplaySuffix ?? string.Empty;

    public async Task SetDisplaySuffixAsync(Guid userId, Guid tenantId, string suffix, CancellationToken cancellationToken)
    {
        if (suffix.Length > 24 || suffix.Contains('\n') || suffix.Contains('\r'))
            throw new InvalidOperationException("Display suffix is invalid.");
        await profiles.PutAsync($"{userId}:{tenantId}", new UserTenantProfileRecord
        {
            UserId = userId, TenantId = tenantId, DisplaySuffix = suffix.Trim()
        }, cancellationToken);
    }

    public Task<string> DisplayNameAsync(Guid userId, Guid tenantId, string fallback,
        CancellationToken cancellationToken) => GetDisplayNameInternalAsync(userId, tenantId, fallback, cancellationToken);

    public async Task<AuthSessionRecord> StartTenantSessionAsync(Guid userId, Guid tenantId,
        CancellationToken cancellationToken, string authMethod = "Password")
    {
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        if (policy.Mode == "required" && authMethod != "Oidc")
            throw new UnauthorizedAccessException("This tenant requires OIDC SSO login.");
        return await CreateSessionAsync(userId, tenantId, cancellationToken, authMethod: authMethod);
    }

    private async Task<AuthSessionRecord> CreateSessionAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken,
        TimeSpan? lifetime = null, string authMethod = "Password")
    {
        var now = clock.GetUtcNow();
        var policy = await LoadPolicyAsync(tenantId, cancellationToken);
        var effectiveLifetime = lifetime ?? TimeSpan.FromHours(12);
        if (effectiveLifetime > TimeSpan.FromMinutes(policy.MaxSessionMinutes))
            effectiveLifetime = TimeSpan.FromMinutes(policy.MaxSessionMinutes);
        var session = new AuthSessionRecord
        {
            UserId = userId, TenantId = tenantId, CreatedAt = now, LastActivityAt = now,
            ExpiresAt = now.Add(effectiveLifetime), AuthMethod = authMethod,
            IdleTimeoutMinutes = policy.IdleTimeoutMinutes
        };
        await sessions.PutAsync(session.Id.ToString(), session, cancellationToken);
        return session;
    }

    private async Task<IReadOnlyList<string>> IssueRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var codes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(8))).ToArray();
        foreach (var code in codes)
        {
            var record = new RecoveryCodeRecord { UserId = userId, CodeHash = HashRecoveryCode(code) };
            await recoveryCodes.PutAsync(record.Id.ToString(), record, cancellationToken);
        }
        return codes;
    }

    private async Task<string> GetDisplayNameInternalAsync(Guid userId, Guid tenantId, string fallback,
        CancellationToken cancellationToken)
    {
        var suffix = await GetDisplaySuffixAsync(userId, tenantId, cancellationToken);
        return string.IsNullOrWhiteSpace(suffix) ? fallback : $"{fallback} {suffix}";
    }

    private static string HashPassword(string password) => PasswordHasher.Hash(password);
    private static bool VerifyPassword(string password, string stored) => PasswordHasher.Verify(password, stored);

    private static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()))).ToLowerInvariant();
}

public sealed class LoginRateExceededException : Exception
{
    public LoginRateExceededException() : base("Too many authentication attempts. Try again later.") { }
}
