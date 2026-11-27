namespace RegentAgent.ControlPlane.Realtime;

public interface ITenantSessionValidator
{
    /// <summary>
    /// Validates session authority, Active tenant status, employment epoch and realtime eligibility.
    /// </summary>
    Task<bool> ValidateAsync(Guid sessionId, Guid userId, Guid tenantId, string? employeeId,
        Guid? credentialEpoch, CancellationToken cancellationToken);
}
