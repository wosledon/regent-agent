using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
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

    private sealed class StubRemoteIdentityProvider : IExternalIdentityProvider
    {
        public string Provider => "Oidc";
        public Task<string> BuildAuthorizationUrlAsync(ExternalAuthorizationParameters parameters,
            ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromResult($"https://idp.example.test/authorize?state={parameters.State}");
        public Task<ExternalIdentityResult> AuthenticateAsync(ExternalIdentityRequest request,
            ExternalIdentityProviderConfiguration configuration, CancellationToken cancellationToken) =>
            Task.FromResult(new ExternalIdentityResult(configuration.Issuer, "subject-1", "member@remote.test", true,
                "Member", new Dictionary<string, string>()));
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
        new JsonDocumentStore<ExternalLoginTransactionRecord>(Path.Combine(root, "external-transactions.json")), resolver);
}
