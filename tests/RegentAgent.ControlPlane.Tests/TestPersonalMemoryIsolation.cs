using RegentAgent.Memory;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class TestPersonalMemoryIsolation : IPersonalMemoryIsolation
{
    public Task IsolatePersonalClaimsAsync(string tenantId, string author, CancellationToken cancellationToken,
        string? authorEmployeeId = null, string? authorUserId = null, bool allowLegacyNameFallback = false,
        string? authorEmploymentPeriodId = null, DateTimeOffset? createdBefore = null) => Task.CompletedTask;
}
