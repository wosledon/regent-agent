using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class BillingServiceTests
{
    [Fact]
    public async Task Local_payment_activates_plan_once_and_appends_one_ledger_entry()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var (tenant, user) = await SeedOwnerAsync(db);
            var service = CreateService(db, root);
            var initial = await service.GetSummaryAsync(user, tenant, CancellationToken.None);
            Assert.Equal("starter", initial.Subscription.PlanId);
            var invoice = await service.CheckoutAsync(user, tenant, new CheckoutRequest("pro"), CancellationToken.None);
            Assert.Equal("Pending", invoice.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PayLocalAsync(user, tenant, invoice.Id,
                new LocalPaymentRequest("yes"), CancellationToken.None));
            var paid = await service.PayLocalAsync(user, tenant, invoice.Id, new LocalPaymentRequest("PAY_LOCAL"), CancellationToken.None);
            Assert.Equal("Paid", paid.Status);
            Assert.StartsWith("local-", paid.PaymentRef);
            await service.PayLocalAsync(user, tenant, invoice.Id, new LocalPaymentRequest("PAY_LOCAL"), CancellationToken.None);
            var summary = await service.GetSummaryAsync(user, tenant, CancellationToken.None);
            Assert.Equal("pro", summary.Subscription.PlanId);
            Assert.Single(summary.Ledger);
            Assert.Equal(29_900, summary.Ledger[0].AmountMinor);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Starter_quota_blocks_additional_projects_and_coowners_cannot_manage_billing()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var (tenant, user) = await SeedOwnerAsync(db);
            for (var i = 0; i < 3; i++)
                db.Projects.Add(new ProjectRecord
                {
                    Id = Guid.NewGuid(), TenantId = tenant, Name = $"Project {i}", Owner = "Owner",
                    Stage = ProjectStage.Proposed, Health = ProjectHealth.OnTrack, Currency = "CNY",
                    TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
                });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = Guid.NewGuid(), Role = "CoOwner" });
            await db.SaveChangesAsync();
            var service = CreateService(db, root);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureQuotaAsync(tenant, "projects", 1, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetSummaryAsync(Guid.NewGuid(), tenant, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Terminated_employees_do_not_consume_active_employee_quota()
    {
        var root = NewRoot();
        try
        {
            await using var db = CreateDb();
            var (tenant, user) = await SeedOwnerAsync(db);
            for (var i = 0; i < 5; i++)
                db.Employees.Add(new EmployeeRecord
                {
                    Id = Guid.NewGuid(), TenantId = tenant, EmployeeNo = $"EMP-{i:D4}", DisplayName = $"Employee {i}",
                    Position = "Engineer", State = i == 4 ? EmployeeState.Terminated : EmployeeState.Active
                });
            await db.SaveChangesAsync();
            var service = CreateService(db, root);

            await service.EnsureQuotaAsync(tenant, "employees", 1, CancellationToken.None);
            var summary = await service.GetSummaryAsync(user, tenant, CancellationToken.None);
            Assert.Equal(4, summary.Quotas.Single(x => x.Resource == "employees").Used);
        }
        finally { Delete(root); }
    }

    private static BillingService CreateService(RegentDbContext db, string root) => new(db,
        new JsonDocumentStore<BillingStateRecord>(Path.Combine(root, "billing.json")),
        new AuditIntegrityService(db, TimeProvider.System), new LocalSubscriptionPaymentAdapter(), TimeProvider.System);

    private static async Task<(Guid Tenant, Guid User)> SeedOwnerAsync(RegentDbContext db)
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenant, Name = "Billing", CompanyName = "Billing Co" });
        db.Users.Add(new UserRecord { Id = user, Email = "owner@billing.test", DisplayName = "Owner" });
        db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, Role = "Owner" });
        await db.SaveChangesAsync();
        return (tenant, user);
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"billing-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
