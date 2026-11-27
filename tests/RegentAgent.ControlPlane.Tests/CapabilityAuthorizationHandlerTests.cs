using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class CapabilityAuthorizationHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task External_member_requires_the_current_membership_capability()
    {
        await using var db = CreateDb();
        var requirement = new CapabilityRequirement("dashboard.read");
        var user = ValidPrincipal("Member", "Oidc", ["dashboard.read"]);

        Assert.False(await AuthorizeAsync(db, user, requirement));

        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Member",
            CapabilitiesJson = "[\"dashboard.read\"]"
        });
        await db.SaveChangesAsync();

        Assert.True(await AuthorizeAsync(db, user, requirement));
    }

    [Fact]
    public async Task Owner_and_explicit_capabilities_keep_access_but_password_is_not_a_bypass()
    {
        await using var db = CreateDb();
        var requirement = new CapabilityRequirement("dashboard.read");
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner"
        });
        await db.SaveChangesAsync();

        Assert.True(await AuthorizeAsync(db, ValidPrincipal("Member", "Password", []), requirement));

        (await db.Memberships.SingleAsync()).Role = "Member";
        (await db.Memberships.SingleAsync()).CapabilitiesJson = "[\"dashboard.read\"]";
        await db.SaveChangesAsync();
        Assert.True(await AuthorizeAsync(db, ValidPrincipal("Member", "Password", []), requirement));

        (await db.Memberships.SingleAsync()).CapabilitiesJson = "[]";
        await db.SaveChangesAsync();
        Assert.False(await AuthorizeAsync(db, ValidPrincipal("Member", "Password", ["dashboard.read"]), requirement));
    }

    [Fact]
    public async Task Development_authentication_is_not_an_authorization_bypass()
    {
        await using var db = CreateDb();
        var requirement = new CapabilityRequirement("dashboard.read");

        Assert.False(await AuthorizeAsync(db, ValidPrincipal("Member", "Development", []), requirement));

        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Member",
            CapabilitiesJson = "[\"dashboard.read\"]"
        });
        await db.SaveChangesAsync();
        Assert.True(await AuthorizeAsync(db, ValidPrincipal("Member", "Development", []), requirement));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("not-a-guid", null)]
    [InlineData(null, "not-a-guid")]
    public async Task Missing_or_invalid_identity_claims_fail_closed(string? tenantClaim, string? userClaim)
    {
        await using var db = CreateDb();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner"
        });
        await db.SaveChangesAsync();
        var user = Principal("Owner", "Oidc", [], tenantClaim, userClaim);

        Assert.False(await AuthorizeAsync(db, user, new CapabilityRequirement("dashboard.read")));
    }

    [Fact]
    public async Task Removed_membership_role_and_capability_are_observed_on_the_next_authorization()
    {
        await using var db = CreateDb();
        var requirement = new CapabilityRequirement("dashboard.read");
        var membership = new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner",
            CapabilitiesJson = "[\"dashboard.read\"]"
        };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        var staleOwner = ValidPrincipal("Owner", "Oidc", ["dashboard.read"]);
        Assert.True(await AuthorizeAsync(db, staleOwner, requirement));

        membership.Role = "Member";
        membership.CapabilitiesJson = "[]";
        await db.SaveChangesAsync();
        Assert.False(await AuthorizeAsync(db, staleOwner, requirement));

        membership.Role = "Member";
        membership.CapabilitiesJson = "[\"dashboard.read\"]";
        await db.SaveChangesAsync();
        Assert.True(await AuthorizeAsync(db, staleOwner, requirement));

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync();
        Assert.False(await AuthorizeAsync(db, staleOwner, requirement));
    }

    private static async Task<bool> AuthorizeAsync(RegentDbContext db, ClaimsPrincipal user,
        CapabilityRequirement requirement)
    {
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await new CapabilityAuthorizationHandler(new MembershipAuthority(db)).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Principal(string role, string? authMethod, string[] capabilities,
        string? tenantClaim = null, string? userClaim = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (tenantClaim is not null) claims.Add(new Claim("tenant_id", tenantClaim));
        if (userClaim is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userClaim));
        if (authMethod is not null) claims.Add(new Claim("auth_method", authMethod));
        claims.AddRange(capabilities.Select(value => new Claim("capability", value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal ValidPrincipal(string role, string? authMethod, string[] capabilities) =>
        Principal(role, authMethod, capabilities, TenantId.ToString(), UserId.ToString());

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
