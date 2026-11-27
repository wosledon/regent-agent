namespace RegentAgent.Agents;

public interface ITenantRuntimeAdmission
{
    Task EnsureActiveAsync(Guid tenantId, CancellationToken cancellationToken);
}
