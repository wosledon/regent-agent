using System.Security.Cryptography;
using System.Text;
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
        var memberships = await db.Memberships.AsNoTracking().Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
        var membership = memberships.FirstOrDefault(x => request.TenantId is null || x.TenantId == request.TenantId)
            ?? memberships.FirstOrDefault() ?? throw new UnauthorizedAccessException("No tenant membership.");
        var session = await CreateSessionAsync(user.Id, membership.TenantId, cancellationToken);
        var displayName = await DisplayNameAsync(user.Id, membership.TenantId, user.DisplayName, cancellationToken);
        return new AuthResult(user.Email, displayName, user.Id, membership.TenantId, session.Id, membership.Role, []);
    }

    public async Task<AuthResult> ExternalLoginAsync(ExternalLoginRequest request, string provider,
        CancellationToken cancellationToken)
    {
        var identity = await providers.GetAsync($"{request.TenantId}:{provider}", cancellationToken);
        if (identity is null || !identity.Enabled || !string.Equals(identity.Issuer, request.Issuer, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Identity provider is not enabled for this tenant.");
        var configuration = new ExternalIdentityProviderConfiguration(identity.Issuer, identity.ClientId,
            identity.ClientSecretRef, identity.RedirectUri, identity.Scopes, identity.GroupClaim);
        var resolver = externalProviders ?? new ExternalIdentityProviderResolver(
            [new LocalExternalIdentityProvider()], new ExternalIdentityOptions());
        var external = await resolver.Resolve(provider).AuthenticateAsync(new ExternalIdentityRequest(
            request.TenantId, provider, request.Issuer, request.Subject, request.Email, request.DisplayName,
            request.Code, request.IdToken, request.CodeVerifier, request.Nonce), configuration, cancellationToken);
        if (!external.EmailVerified || string.IsNullOrWhiteSpace(external.Subject)
            || !string.Equals(external.Issuer, identity.Issuer, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Verified issuer and subject are required.");
        var email = external.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null)
        {
            user = new UserRecord { Id = Guid.NewGuid(), Email = email, DisplayName = external.DisplayName };
            db.Users.Add(user);
        }
        if (!await db.Memberships.AnyAsync(x => x.TenantId == request.TenantId && x.UserId == user.Id, cancellationToken))
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = request.TenantId, UserId = user.Id, Role = "Member" });
        await db.SaveChangesAsync(cancellationToken);
        var session = await CreateSessionAsync(user.Id, request.TenantId, cancellationToken);
        var displayName = await DisplayNameAsync(user.Id, request.TenantId, user.DisplayName, cancellationToken);
        return new AuthResult(user.Email, displayName, user.Id, request.TenantId, session.Id, "Member", []);
    }

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
            .Select(x => new SessionDto(x.Id, x.TenantId, x.CreatedAt, x.ExpiresAt, x.Status)).ToArray();

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

    public Task<AuthSessionRecord> StartTenantSessionAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        CreateSessionAsync(userId, tenantId, cancellationToken);

    private async Task<AuthSessionRecord> CreateSessionAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = new AuthSessionRecord { UserId = userId, TenantId = tenantId, CreatedAt = now, ExpiresAt = now.AddHours(12) };
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

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"100000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[1]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()))).ToLowerInvariant();
}

public sealed class LoginRateExceededException : Exception
{
    public LoginRateExceededException() : base("Too many authentication attempts. Try again later.") { }
}
