using System.Security.Claims;
using RegentAgent.Contracts.Agents;

namespace RegentAgent.ControlPlane.Identity;

public sealed class ToolDisclosureAuthorityFactory(MembershipAuthority membership)
{
    public async Task<ToolDisclosureAuthority> CreateAsync(ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var current = await membership.FindAsync(user, cancellationToken);
        if (current is null)
            return new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);

        var restrictedActivated = current.Role is "Owner" or "CoOwner"
            || current.Capabilities.Contains(ControlPlanePolicies.RestrictedToolActivationCapability);
        return new ToolDisclosureAuthority(current.Capabilities, restrictedActivated);
    }
}
