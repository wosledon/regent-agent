namespace RegentAgent.Memory;

public interface IPersonalMemoryIsolation
{
    Task IsolatePersonalClaimsAsync(string tenantId, string author, CancellationToken cancellationToken,
        string? authorEmployeeId = null, string? authorUserId = null, bool allowLegacyNameFallback = false,
        string? authorEmploymentPeriodId = null, DateTimeOffset? createdBefore = null);
}
