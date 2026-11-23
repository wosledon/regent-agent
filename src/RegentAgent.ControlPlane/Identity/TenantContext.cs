using System.Security.Claims;
using RegentAgent.Contracts.Primitives;

namespace RegentAgent.ControlPlane.Identity;

public sealed class TenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;
    public TenantId TenantId => IsAuthenticated
        ? TenantId.Parse(User.FindFirstValue("tenant_id") ?? throw new InvalidOperationException("Missing tenant claim."))
        : throw new UnauthorizedAccessException("Tenant context requires authentication.");
    public UserId UserId => IsAuthenticated
        ? UserId.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing user claim."))
        : throw new UnauthorizedAccessException("Tenant context requires authentication.");
    public string DisplayName => User.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
}
