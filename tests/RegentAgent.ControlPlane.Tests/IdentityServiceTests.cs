using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Identity;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class IdentityServiceTests
{
    [Fact]
    public async Task Password_registration_recovery_and_session_revocation_work()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root);
            var registered = await service.RegisterAsync(new RegisterRequest("owner@identity.test", "strong-password-123", "Owner", "Identity Co"), CancellationToken.None);
            Assert.Equal(10, registered.RecoveryCodes.Count);
            var credential = await new JsonDocumentStore<CredentialRecord>(Path.Combine(root, "credentials.json"))
                .GetAsync(registered.UserId.ToString(), CancellationToken.None);
            Assert.StartsWith("$argon2id$", credential!.PasswordHash);
            var login = await service.LoginAsync(new LoginRequest("owner@identity.test", "strong-password-123"), CancellationToken.None);
            Assert.Equal(registered.UserId, login.UserId);
            await service.RevokeSessionAsync(registered.UserId, login.SessionId, CancellationToken.None);
            Assert.Contains(await service.ListSessionsAsync(registered.UserId, CancellationToken.None), x => x.Id == login.SessionId && x.Status == "Revoked");
            await service.RecoverAsync(new RecoveryRequest("owner@identity.test", registered.RecoveryCodes[0], "new-password-456"), CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync(new LoginRequest("owner@identity.test", "strong-password-123"), CancellationToken.None));
            var recovered = await service.LoginAsync(new LoginRequest("owner@identity.test", "new-password-456"), CancellationToken.None);
            Assert.NotEqual(login.SessionId, recovered.SessionId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Oidc_invitation_and_display_suffix_are_tenant_scoped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@oidc.test", "strong-password-123", "Owner", "OIDC Co"), CancellationToken.None);
            await service.ConfigureProviderAsync(owner.UserId, owner.TenantId, new IdentityProviderDto(
                owner.TenantId, "Oidc", "https://idp.local/tenant", "client-local", true, "{\"group\":\"Member\"}"), CancellationToken.None);
            var external = await service.ExternalLoginAsync(new ExternalLoginRequest(owner.TenantId,
                "https://idp.local/tenant", "subject-1", "member@oidc.test", "Member"), "Oidc", CancellationToken.None);
            Assert.Equal(owner.TenantId, external.TenantId);
            var invitation = await service.CreateInvitationAsync(owner.UserId, owner.TenantId,
                new InvitationRequest("invitee@oidc.test", "CoOwner", 24), CancellationToken.None);
            var invitee = await service.RegisterAsync(new RegisterRequest("invitee@oidc.test", "strong-password-123", "Invitee", "Invitee Co"), CancellationToken.None);
            await service.AcceptInvitationAsync(invitee.UserId, invitation.Token!, CancellationToken.None);
            await service.SetDisplaySuffixAsync(invitee.UserId, owner.TenantId, "[Boss]", CancellationToken.None);
            var displayName = await service.DisplayNameAsync(invitee.UserId, owner.TenantId, "Invitee", CancellationToken.None);
            Assert.Equal("Invitee [Boss]", displayName);
            Assert.Contains(await db.Memberships.ToListAsync(), x => x.TenantId == owner.TenantId && x.UserId == invitee.UserId && x.Role == "CoOwner");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Remote_identity_requires_invitation_and_persists_subject_binding()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-remote-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var resolver = new ExternalIdentityProviderResolver([new StubRemoteIdentityProvider()],
                new ExternalIdentityOptions { Mode = "remote" });
            var service = CreateService(db, root, resolver);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@remote.test", "strong-password-123", "Owner", "Remote Co"), CancellationToken.None);
            await service.ConfigureProviderAsync(owner.UserId, owner.TenantId, new IdentityProviderDto(
                owner.TenantId, "Oidc", "https://idp.example.test/tenant", "client", true, "{}",
                RedirectUri: "https://app.example.test/auth/oidc/callback"), CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExternalLoginAsync(
                new ExternalLoginRequest(owner.TenantId, "https://idp.example.test/tenant", "subject-1", "member@remote.test", "Member"),
                "Oidc", CancellationToken.None));
            var invitation = await service.CreateInvitationAsync(owner.UserId, owner.TenantId,
                new InvitationRequest("member@remote.test", "CoOwner", 24), CancellationToken.None);
            var firstStart = await service.StartExternalLoginAsync(owner.TenantId, "oidc", null, CancellationToken.None);
            var first = await service.CompleteExternalLoginAsync("oidc", firstStart.TransactionId,
                new ExternalCallbackRequest("code-1", firstStart.State), CancellationToken.None);
            Assert.Equal("CoOwner", first.Role);
            Assert.Equal("member@remote.test", first.Email);

            var secondStart = await service.StartExternalLoginAsync(owner.TenantId, "oidc", null, CancellationToken.None);
            var second = await service.CompleteExternalLoginAsync("oidc", secondStart.TransactionId,
                new ExternalCallbackRequest("code-2", secondStart.State), CancellationToken.None);
            Assert.Equal(first.UserId, second.UserId);
            Assert.Equal("Accepted", (await service.ListInvitationsAsync(owner.UserId, owner.TenantId, CancellationToken.None))
                .Single(x => x.Id == invitation.Id).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Required_sso_requires_emergency_access_and_revokes_password_sessions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-policy-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@required.test", "strong-password-123", "Owner", "Required Co"), CancellationToken.None);
            await service.ConfigureProviderAsync(owner.UserId, owner.TenantId, new IdentityProviderDto(
                owner.TenantId, "Oidc", "https://idp.required.test/tenant", "client", true, "{}"), CancellationToken.None);
            var required = new AuthenticationPolicyDto("required", true, [], [], 60, 480, 30, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UpdateAuthenticationPolicyAsync(owner.UserId, owner.TenantId, required, CancellationToken.None));
            var passwordSession = await service.LoginAsync(new LoginRequest("owner@required.test", "strong-password-123"), CancellationToken.None);
            var emergency = await service.ConfigureEmergencyAccessAsync(owner.UserId, owner.TenantId, CancellationToken.None);
            await service.UpdateAuthenticationPolicyAsync(owner.UserId, owner.TenantId, required, CancellationToken.None);
            Assert.Contains(await service.ListSessionsAsync(owner.UserId, CancellationToken.None), x =>
                x.Id == passwordSession.SessionId && x.Status == "Revoked");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                service.LoginAsync(new LoginRequest("owner@required.test", "strong-password-123"), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.ConfigureProviderAsync(owner.UserId, owner.TenantId,
                    new IdentityProviderDto(owner.TenantId, "Oidc", "https://idp.required.test/tenant", "client", false, "{}"), CancellationToken.None));
            Assert.NotEmpty(emergency.Credential);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Oidc_policy_enforces_acr_amr_auth_time_and_groups_and_revokes_on_mapping_change()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-assurance-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var external = new StubRemoteIdentityProvider();
            var resolver = new ExternalIdentityProviderResolver([external], new ExternalIdentityOptions { Mode = "remote" });
            var service = CreateService(db, root, resolver);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@assurance.test", "strong-password-123", "Owner", "Assurance Co"), CancellationToken.None);
            external.Email = owner.Email;
            await service.ConfigureProviderAsync(owner.UserId, owner.TenantId, new IdentityProviderDto(
                owner.TenantId, "Oidc", "https://idp.assurance.test/tenant", "client", true,
                "{\"engineering\":\"dashboard.read\"}"), CancellationToken.None);
            var firstStart = await service.StartExternalLoginAsync(owner.TenantId, "oidc", null, CancellationToken.None);
            var first = await service.CompleteExternalLoginAsync("oidc", firstStart.TransactionId,
                new ExternalCallbackRequest("code-1", firstStart.State), CancellationToken.None);
            Assert.Contains("dashboard.read", first.Capabilities);
            await service.UpdateAuthenticationPolicyAsync(owner.UserId, owner.TenantId,
                new AuthenticationPolicyDto("optional", true, ["urn:mfa"], ["mfa"], 60, 480, 30, 1), CancellationToken.None);

            external.Acr = "urn:password";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CompleteExternalAsync(service, owner.TenantId));
            external.Acr = "urn:mfa";
            external.Amr = ["pwd"];
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CompleteExternalAsync(service, owner.TenantId));
            external.Amr = ["pwd", "mfa"];
            external.AuthTime = DateTimeOffset.UtcNow.AddMinutes(-61);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CompleteExternalAsync(service, owner.TenantId));
            external.AuthTime = DateTimeOffset.UtcNow;
            external.Groups = [];
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CompleteExternalAsync(service, owner.TenantId));

            external.Groups = ["engineering"];
            var accepted = await CompleteExternalAsync(service, owner.TenantId);
            Assert.Contains("dashboard.read", accepted.Capabilities);
            await service.UpdateGroupMappingAsync(owner.UserId, owner.TenantId, "{}", CancellationToken.None);
            Assert.Contains(await service.ListSessionsAsync(owner.UserId, CancellationToken.None), x =>
                x.Id == accepted.SessionId && x.Status == "Revoked");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Group_mapping_cannot_grant_owner_or_high_impact_capabilities()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-mapping-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@mapping.test", "strong-password-123", "Owner", "Mapping Co"), CancellationToken.None);
            await service.ConfigureProviderAsync(owner.UserId, owner.TenantId, new IdentityProviderDto(
                owner.TenantId, "Oidc", "https://idp.mapping.test/tenant", "client", true, "{}"), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateGroupMappingAsync(
                owner.UserId, owner.TenantId, "{\"admins\":\"owner\"}", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateGroupMappingAsync(
                owner.UserId, owner.TenantId, "{\"admins\":\"models:write\"}", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Emergency_access_is_single_use_rotated_and_creates_a_restricted_fifteen_minute_session()
    {
        var root = Path.Combine(Path.GetTempPath(), $"identity-emergency-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root);
            var owner = await service.RegisterAsync(new RegisterRequest("owner@emergency.test", "strong-password-123", "Owner", "Emergency Co"), CancellationToken.None);
            var configured = await service.ConfigureEmergencyAccessAsync(owner.UserId, owner.TenantId, CancellationToken.None);
            var before = DateTimeOffset.UtcNow;
            var used = await service.UseEmergencyAccessAsync(new EmergencyAccessRequest(
                owner.TenantId, configured.Credential, "Restore unavailable IdP"), CancellationToken.None);
            Assert.True(used.ExpiresAt >= before.AddMinutes(15) && used.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
            Assert.NotEqual(configured.Credential, used.RotatedCredential);
            Assert.Equal("Emergency", used.SessionKind);
            var session = await new JsonDocumentStore<AuthSessionRecord>(Path.Combine(root, "sessions.json"))
                .GetAsync(used.SessionId.ToString(), CancellationToken.None);
            Assert.Equal("Emergency", session!.SessionKind);
            Assert.Equal("Emergency", session.AuthMethod);
            Assert.Equal(15, session.IdleTimeoutMinutes);
            Assert.Contains("sso.restore", session.AllowedActionsJson);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.UseEmergencyAccessAsync(
                new EmergencyAccessRequest(owner.TenantId, configured.Credential, "Replay"), CancellationToken.None));
            var rotated = await service.UseEmergencyAccessAsync(new EmergencyAccessRequest(
                owner.TenantId, used.RotatedCredential, "Second controlled use"), CancellationToken.None);
            Assert.NotEqual(used.SessionId, rotated.SessionId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static Task<AuthResult> CompleteExternalAsync(IdentityService service, Guid tenantId) =>
        service.StartExternalLoginAsync(tenantId, "oidc", null, CancellationToken.None)
            .ContinueWith(async start => await service.CompleteExternalLoginAsync("oidc", start.Result.TransactionId,
                new ExternalCallbackRequest("code", start.Result.State), CancellationToken.None)).Unwrap();

    private sealed class StubRemoteIdentityProvider : IExternalIdentityProvider
    {
        public string Provider => "Oidc";
        public string Email { get; set; } = "member@remote.test";
        public IReadOnlyList<string> Groups { get; set; } = ["engineering"];
        public string? Acr { get; set; } = "urn:mfa";
        public IReadOnlyList<string> Amr { get; set; } = ["pwd", "mfa"];
        public DateTimeOffset? AuthTime { get; set; } = DateTimeOffset.UtcNow;
        public Task<string> BuildAuthorizationUrlAsync(ExternalAuthorizationParameters parameters,
            ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromResult($"https://idp.example.test/authorize?state={parameters.State}");
        public Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
            ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromResult(new ExternalIdentityResult(configuration.Issuer, "subject-1", Email, true,
                "Member", new Dictionary<string, string>())
            {
                AuthTime = AuthTime, Groups = Groups, Acr = Acr, Amr = Amr
            });
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IdentityService CreateService(RegentDbContext db, string root,
        IExternalIdentityProviderResolver? resolver = null) => new(
        db, TimeProvider.System, new LoginRateLimiter(),
        new JsonDocumentStore<CredentialRecord>(Path.Combine(root, "credentials.json")),
        new JsonDocumentStore<AuthSessionRecord>(Path.Combine(root, "sessions.json")),
        new JsonDocumentStore<RecoveryCodeRecord>(Path.Combine(root, "recovery.json")),
        new JsonDocumentStore<UserTenantProfileRecord>(Path.Combine(root, "profiles.json")),
        new JsonDocumentStore<IdentityProviderRecord>(Path.Combine(root, "providers.json")),
        new JsonDocumentStore<InvitationRecord>(Path.Combine(root, "invitations.json")),
        new JsonDocumentStore<OwnershipDelegationRecord>(Path.Combine(root, "delegations.json")),
        new JsonDocumentStore<ExternalIdentityBindingRecord>(Path.Combine(root, "external-bindings.json")),
        new JsonDocumentStore<ExternalLoginTransactionRecord>(Path.Combine(root, "external-transactions.json")),
        new JsonDocumentStore<AuthenticationPolicyRecord>(Path.Combine(root, "authentication-policies.json")),
        new JsonDocumentStore<EmergencyAccessCredentialRecord>(Path.Combine(root, "emergency-access-credentials.json")),
        new AuditIntegrityService(db, TimeProvider.System), new TenantRlsScope(db), resolver);
}
