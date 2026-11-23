using RegentAgent.Contracts.Primitives;

namespace RegentAgent.ControlPlane.Identity;

public interface ITenantContext
{
    TenantId TenantId { get; }
    UserId UserId { get; }
    string DisplayName { get; }
    bool IsAuthenticated { get; }
}
