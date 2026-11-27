using System.Collections.Concurrent;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class TestMembershipRegistry
{
    private readonly ConcurrentDictionary<(Guid TenantId, Guid UserId), byte> managed = new();

    public bool TryMark(Guid tenantId, Guid userId) => managed.TryAdd((tenantId, userId), 0);
}
