using Microsoft.AspNetCore.Authorization;

namespace RegentAgent.ControlPlane.Identity;

public sealed record CurrentMembershipRequirement : IAuthorizationRequirement;

public sealed class CurrentMembershipAuthorizationHandler(MembershipAuthority membership)
    : AuthorizationHandler<CurrentMembershipRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CurrentMembershipRequirement requirement)
    {
        if (await membership.FindAsync(context.User) is not null)
            context.Succeed(requirement);
    }
}

public sealed record MembershipRoleRequirement(params string[] AllowedRoles) : IAuthorizationRequirement;

public sealed class MembershipRoleAuthorizationHandler(MembershipAuthority membership)
    : AuthorizationHandler<MembershipRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        MembershipRoleRequirement requirement)
    {
        var current = await membership.FindAsync(context.User);
        if (current is not null && requirement.AllowedRoles.Contains(current.Role, StringComparer.Ordinal))
            context.Succeed(requirement);
    }
}
