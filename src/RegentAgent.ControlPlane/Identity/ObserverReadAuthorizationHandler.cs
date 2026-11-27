using Microsoft.AspNetCore.Authorization;
using RegentAgent.Chat;

namespace RegentAgent.ControlPlane.Identity;

public sealed record ObserverReadRequirement(string Capability) : IAuthorizationRequirement;

public static class ObserverReadPolicy
{
    public static bool CanRead(ResolvedChatViewer? viewer, string capability) =>
        viewer is not null
        && (viewer.Viewer is OwnerObserverViewer
            || viewer.Membership.Capabilities.Contains(capability));
}

public sealed class ObserverReadAuthorizationHandler(MembershipAuthority membership)
    : AuthorizationHandler<ObserverReadRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ObserverReadRequirement requirement)
    {
        if (ObserverReadPolicy.CanRead(await membership.ResolveChatViewerAsync(context.User), requirement.Capability))
            context.Succeed(requirement);
    }
}
