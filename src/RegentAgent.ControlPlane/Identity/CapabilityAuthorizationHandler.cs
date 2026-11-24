using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace RegentAgent.ControlPlane.Identity;

public sealed record CapabilityRequirement(string Capability) : IAuthorizationRequirement;

public sealed class CapabilityAuthorizationHandler : AuthorizationHandler<CapabilityRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        CapabilityRequirement requirement)
    {
        if (context.User.IsInRole("Owner") || context.User.IsInRole("CoOwner"))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
        var authMethod = context.User.FindFirstValue("auth_method");
        if (authMethod is null || authMethod is "Password" or "Development")
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
        if (context.User.FindAll("capability").Any(claim =>
            claim.Value.Equals(requirement.Capability, StringComparison.OrdinalIgnoreCase)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
