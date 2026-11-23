using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed record TenantAccount(string Id, string Name, string Status, int Employees, int RunnerCount);
public sealed record SupportGrantRecord(string Id, string TenantId, string OperatorId, string Scope, DateTimeOffset ExpiresAt, string Status);
public sealed record BackupProfileRecord(string Id, string DeploymentId, bool Enabled, string Components, int RetentionDays);
public sealed record OpsAuditRecord(string Id, string OperatorId, string Action, string Target, DateTimeOffset CreatedAt);

public sealed class OperationsService(
    JsonDocumentStore<TenantAccount> tenants,
    JsonDocumentStore<SupportGrantRecord> grants,
    JsonDocumentStore<BackupProfileRecord> backups,
    JsonDocumentStore<OpsAuditRecord> audits)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await tenants.GetAllAsync(cancellationToken)).Count == 0)
            await tenants.PutAsync("demo", new TenantAccount("demo", "Regent Demo", "Active", 4, 1), cancellationToken);
        if ((await backups.GetAllAsync(cancellationToken)).Count == 0)
            await backups.PutAsync("default", new BackupProfileRecord("default", "local", false, "none", 0), cancellationToken);
    }

    public Task<IReadOnlyList<TenantAccount>> TenantsAsync(CancellationToken cancellationToken) => tenants.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<SupportGrantRecord>> GrantsAsync(CancellationToken cancellationToken) => grants.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<BackupProfileRecord>> BackupsAsync(CancellationToken cancellationToken) => backups.GetAllAsync(cancellationToken);
    public Task<IReadOnlyList<OpsAuditRecord>> AuditsAsync(CancellationToken cancellationToken) => audits.GetAllAsync(cancellationToken);

    public async Task<SupportGrantRecord> CreateGrantAsync(string tenantId, string operatorId, string scope, int minutes, CancellationToken cancellationToken)
    {
        var grant = new SupportGrantRecord(Guid.NewGuid().ToString("N"), tenantId, operatorId, scope,
            DateTimeOffset.UtcNow.AddMinutes(minutes), "Pending");
        await grants.PutAsync(grant.Id, grant, cancellationToken);
        await AuditAsync(operatorId, "support_grant.created", tenantId, cancellationToken);
        return grant;
    }

    public async Task<SupportGrantRecord> ApproveGrantAsync(string id, string operatorId, CancellationToken cancellationToken)
    {
        var grant = await grants.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Grant not found.");
        if (grant.Status != "Pending") throw new InvalidOperationException("Only pending grants can be approved.");
        grant = grant with { Status = "Active" };
        await grants.PutAsync(id, grant, cancellationToken);
        await AuditAsync(operatorId, "support_grant.approved", id, cancellationToken);
        return grant;
    }

    public async Task<TenantAccount> SetTenantStatusAsync(string tenantId, string status, string operatorId,
        CancellationToken cancellationToken)
    {
        if (status is not ("Active" or "Suspended")) throw new InvalidOperationException("Invalid tenant status.");
        var tenant = await tenants.GetAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException("Tenant not found.");
        tenant = tenant with { Status = status };
        await tenants.PutAsync(tenantId, tenant, cancellationToken);
        await AuditAsync(operatorId, "tenant.status_changed", tenantId, cancellationToken);
        return tenant;
    }

    public async Task RevokeGrantAsync(string id, string operatorId, CancellationToken cancellationToken)
    {
        var grant = await grants.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Grant not found.");
        await grants.PutAsync(id, grant with { Status = "Revoked" }, cancellationToken);
        await AuditAsync(operatorId, "support_grant.revoked", id, cancellationToken);
    }

    public async Task SetBackupAsync(bool enabled, string components, string operatorId, CancellationToken cancellationToken)
    {
        var profile = new BackupProfileRecord("default", "local", enabled, components, enabled ? 35 : 0);
        await backups.PutAsync(profile.Id, profile, cancellationToken);
        await AuditAsync(operatorId, "backup.updated", "local", cancellationToken);
    }

    private async Task AuditAsync(string operatorId, string action, string target, CancellationToken cancellationToken)
    {
        var record = new OpsAuditRecord(Guid.NewGuid().ToString("N"), operatorId, action, target, DateTimeOffset.UtcNow);
        await audits.PutAsync(record.Id, record, cancellationToken);
    }
}
