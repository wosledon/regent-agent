using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using RegentAgent.ControlPlane.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class CapabilityAuthorizationHandlerTests
{
    [Fact]
    public async Task External_member_requires_explicit_group_capability()
    {
        var requirement = new CapabilityRequirement("dashboard.read");
        var denied = await AuthorizeAsync("Member", "Oidc", []);
        Assert.False(denied);
        var allowed = await AuthorizeAsync("Member", "Oidc", ["dashboard.read"]);
        Assert.True(allowed);
    }

    [Fact]
    public async Task Owner_and_local_member_keep_existing_role_access()
    {
        Assert.True(await AuthorizeAsync("Owner", "Oidc", []));
        Assert.True(await AuthorizeAsync("Member", "Password", []));
    }

    private static async Task<bool> AuthorizeAsync(string role, string authMethod, string[] capabilities)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, role), new("auth_method", authMethod)
        };
        claims.AddRange(capabilities.Select(value => new Claim("capability", value)));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requirement = new CapabilityRequirement("dashboard.read");
        var context = new AuthorizationHandlerContext([requirement], user, null);
        await new CapabilityAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }
}
