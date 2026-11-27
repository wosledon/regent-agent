using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Agents;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class TenantPurgeServiceTests
{
    [Fact]
    public async Task Purge_deletes_registered_tenant_json_stores_and_runtime_relational_records()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        db.Tenants.AddRange(
            new TenantRecord { Id = tenantId, Name = "purge-json", CompanyName = "Purge JSON Co" },
            new TenantRecord { Id = otherTenantId, Name = "retain-json", CompanyName = "Retain JSON Co" });
        await db.SaveChangesAsync(CancellationToken.None);

        var stores = scope.ServiceProvider.GetServices<ITenantJsonDocumentStore>().ToArray();
        Assert.Contains(stores, store => store is JsonDocumentStore<ExternalIdentityBindingRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<ExternalLoginTransactionRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<AuthenticationPolicyRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<EmergencyAccessCredentialRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<BillingStateRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<AlertRecord>);
        Assert.Contains(stores, store => store is JsonDocumentStore<AgentContextSnapshot>);
        Assert.DoesNotContain(stores, store => store is JsonDocumentStore<DataLifecycleState>);

        var bindings = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<ExternalIdentityBindingRecord>>();
        var transactions = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<ExternalLoginTransactionRecord>>();
        var policies = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<AuthenticationPolicyRecord>>();
        var emergency = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<EmergencyAccessCredentialRecord>>();
        var billing = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<BillingStateRecord>>();
        var alerts = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<AlertRecord>>();
        var contexts = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<AgentContextSnapshot>>();
        var taskId = Guid.NewGuid();
        var retainedTaskId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await bindings.PutAsync("binding-1", new ExternalIdentityBindingRecord { TenantId = tenantId }, CancellationToken.None);
        await transactions.PutAsync("transaction-1", new ExternalLoginTransactionRecord { TenantId = tenantId }, CancellationToken.None);
        await policies.PutAsync(tenantId.ToString(), new AuthenticationPolicyRecord { TenantId = tenantId }, CancellationToken.None);
        await emergency.PutAsync("emergency-1", new EmergencyAccessCredentialRecord { TenantId = tenantId }, CancellationToken.None);
        await billing.PutAsync(tenantId.ToString(), new BillingStateRecord
        {
            TenantId = tenantId,
            Subscription = new SubscriptionDto(tenantId, "starter", "Active", "CNY", now, now.AddMonths(1), 1)
        }, CancellationToken.None);
        await alerts.PutAsync("alert-1", new AlertRecord { TenantId = tenantId.ToString() }, CancellationToken.None);
        await contexts.PutAsync($"{tenantId}:{taskId}", new AgentContextSnapshot { TaskId = taskId }, CancellationToken.None);
        await contexts.PutAsync($"{otherTenantId}:{retainedTaskId}", new AgentContextSnapshot { TaskId = retainedTaskId }, CancellationToken.None);

        db.IdempotencyRecords.AddRange(
            new IdempotencyRecord { Id = Guid.NewGuid(), TenantId = tenantId, Key = "key", Operation = "op", RequestHash = "hash" },
            new IdempotencyRecord { Id = Guid.NewGuid(), TenantId = otherTenantId, Key = "key", Operation = "op", RequestHash = "hash" });
        db.Outbox.AddRange(
            new OutboxRecord { Id = Guid.NewGuid(), TenantId = tenantId, EventType = "test", CreatedAt = now },
            new OutboxRecord { Id = Guid.NewGuid(), TenantId = otherTenantId, EventType = "test", CreatedAt = now });
        db.Inbox.AddRange(
            new InboxRecord { Id = Guid.NewGuid(), TenantId = tenantId, MessageId = Guid.NewGuid(), EventType = "test", CreatedAt = now },
            new InboxRecord { Id = Guid.NewGuid(), TenantId = otherTenantId, MessageId = Guid.NewGuid(), EventType = "test", CreatedAt = now });
        await db.SaveChangesAsync(CancellationToken.None);

        await scope.ServiceProvider.GetRequiredService<TenantPurgeService>().PurgeAsync(tenantId, CancellationToken.None);

        Assert.DoesNotContain(await bindings.GetAllAsync(CancellationToken.None), item => item.TenantId == tenantId);
        Assert.DoesNotContain(await transactions.GetAllAsync(CancellationToken.None), item => item.TenantId == tenantId);
        Assert.Null(await policies.GetAsync(tenantId.ToString(), CancellationToken.None));
        Assert.DoesNotContain(await emergency.GetAllAsync(CancellationToken.None), item => item.TenantId == tenantId);
        Assert.Null(await billing.GetAsync(tenantId.ToString(), CancellationToken.None));
        Assert.DoesNotContain(await alerts.GetAllAsync(CancellationToken.None), item => item.TenantId == tenantId.ToString());
        Assert.Null(await contexts.GetAsync($"{tenantId}:{taskId}", CancellationToken.None));
        Assert.NotNull(await contexts.GetAsync($"{otherTenantId}:{retainedTaskId}", CancellationToken.None));
        Assert.DoesNotContain(await db.IdempotencyRecords.AsNoTracking().ToListAsync(), item => item.TenantId == tenantId);
        Assert.DoesNotContain(await db.Outbox.AsNoTracking().ToListAsync(), item => item.TenantId == tenantId);
        Assert.DoesNotContain(await db.Inbox.AsNoTracking().ToListAsync(), item => item.TenantId == tenantId);
        Assert.Single(await db.IdempotencyRecords.AsNoTracking().Where(item => item.TenantId == otherTenantId).ToListAsync());
        Assert.Single(await db.Outbox.AsNoTracking().Where(item => item.TenantId == otherTenantId).ToListAsync());
        Assert.Single(await db.Inbox.AsNoTracking().Where(item => item.TenantId == otherTenantId).ToListAsync());
    }
}
