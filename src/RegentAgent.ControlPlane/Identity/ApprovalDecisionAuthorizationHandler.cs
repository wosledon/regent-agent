using Microsoft.AspNetCore.Authorization;

namespace RegentAgent.ControlPlane.Identity;

public sealed record ApprovalDecisionRequirement : IAuthorizationRequirement;

public sealed class ApprovalDecisionAuthorizationHandler(MembershipAuthority membership)
    : AuthorizationHandler<ApprovalDecisionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ApprovalDecisionRequirement requirement)
    {
        var current = await membership.FindAsync(context.User);
        if (current is null) return;
        if (current.Role is "Owner" or "CoOwner"
            || current.Capabilities.Any(value => value.StartsWith("approval.", StringComparison.OrdinalIgnoreCase)))
            context.Succeed(requirement);
    }
}
