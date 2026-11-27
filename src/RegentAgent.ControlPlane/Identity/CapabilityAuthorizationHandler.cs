using Microsoft.AspNetCore.Authorization;

namespace RegentAgent.ControlPlane.Identity;

public sealed record CapabilityRequirement(string Capability) : IAuthorizationRequirement;

public sealed class CapabilityAuthorizationHandler(MembershipAuthority membership)
    : AuthorizationHandler<CapabilityRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CapabilityRequirement requirement)
    {
        var current = await membership.FindAsync(context.User);
        if (current is null) return;
        if (current.Role is "Owner" or "CoOwner"
            || current.Capabilities.Contains(requirement.Capability))
            context.Succeed(requirement);
    }
}
