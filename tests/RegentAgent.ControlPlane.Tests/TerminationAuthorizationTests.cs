using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TerminationAuthorizationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ApprovalState.Pending, "employee.terminate", 60, true, false)]
    [InlineData(ApprovalState.Rejected, "employee.terminate", 60, true, false)]
    [InlineData(ApprovalState.Approved, "employee.terminate", -1, true, false)]
    [InlineData(ApprovalState.Approved, "employee.offboard", 60, true, false)]
    [InlineData(ApprovalState.Approved, "employee.terminate", 60, false, false)]
    [InlineData(ApprovalState.Approved, "employee.terminate", 60, true, true)]
    public async Task Termination_requires_same_tenant_approved_unexpired_employee_terminate_approval(
        ApprovalState state, string actionType, int expiresInMinutes, bool sameTenantApproval, bool expected)
    {
        await using var db = CreateDb();
        var employeeId = await AddEmployeeAsync(db, TenantId);
        var approval = AddApproval(db, sameTenantApproval ? TenantId : Guid.NewGuid(), state, actionType,
            Now.AddMinutes(expiresInMinutes), employeeId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        if (expected)
            await service.TerminateAsync(employeeId, approval.Id.ToString(), "execute-1", CancellationToken.None);
        else
            await Assert.ThrowsAnyAsync<Exception>(() => service.TerminateAsync(employeeId, approval.Id.ToString(),
                "execute-1", CancellationToken.None));
    }

    [Fact]
    public async Task Termination_requires_an_approval_id_and_a_tenant_owned_target_employee()
    {
        await using var db = CreateDb();
        var employeeId = await AddEmployeeAsync(db, TenantId);
        var approval = AddApproval(db, TenantId, ApprovalState.Approved, "employee.terminate", Now.AddHours(1), employeeId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId, null!,
            "execute-1", CancellationToken.None));
        var otherTenantEmployee = await AddEmployeeAsync(db, Guid.NewGuid());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(otherTenantEmployee,
            approval.Id.ToString(), "execute-1", CancellationToken.None));
    }

    [Fact]
    public async Task Termination_approval_is_bound_to_target_and_consumed_once()
    {
        await using var db = CreateDb();
        var employeeId = await AddEmployeeAsync(db, TenantId);
        var otherEmployeeId = await AddEmployeeAsync(db, TenantId);
        var approval = AddApproval(db, TenantId, ApprovalState.Approved, "employee.terminate", Now.AddHours(1), otherEmployeeId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
            approval.Id.ToString(), "execute-1", CancellationToken.None));

        approval = await db.Approvals.SingleAsync();
        approval.TargetEmployeeId = employeeId;
        approval.ActionHash = new ApprovalBinding(TenantId, ApprovalActionTypes.EmployeeTerminate,
            TargetEmployeeId: employeeId).ComputeV2Hash();
        await db.SaveChangesAsync();
        await service.TerminateAsync(employeeId, approval.Id.ToString(), "execute-1", CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
            approval.Id.ToString(), "execute-2", CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_v1_hash_is_readable_but_never_executable()
    {
        await using var db = CreateDb();
        var employeeId = await AddEmployeeAsync(db, TenantId);
        var approval = AddApproval(db, TenantId, ApprovalState.Approved, "employee.terminate", Now.AddHours(1), employeeId);
        approval.ActionHash = ApprovalService.ComputeActionHash(ApprovalActionTypes.EmployeeTerminate, employeeId);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
            approval.Id.ToString(), "execute-1", CancellationToken.None));
        Assert.Equal(EmployeeState.Active, (await db.Employees.SingleAsync()).State);
    }

    private static ApprovalRecord AddApproval(RegentDbContext db, Guid tenantId, ApprovalState state,
        string actionType, DateTimeOffset expiresAt, Guid targetEmployeeId)
    {
        var binding = new ApprovalBinding(tenantId, actionType, TargetEmployeeId: targetEmployeeId);
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = actionType, Title = "Termination",
            Impact = "Ends employment", Severity = Severity.High, State = state, Requester = "Requester",
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = targetEmployeeId,
            ActionHash = binding.ComputeV2Hash(), DecidedBy = Guid.NewGuid(), DecidedAt = Now.AddMinutes(-2),
            CreatedAt = Now.AddHours(-1), ExpiresAt = expiresAt
        };
        db.Approvals.Add(approval);
        return approval;
    }

    private static async Task<Guid> AddEmployeeAsync(RegentDbContext db, Guid tenantId)
    {
        var employee = new EmployeeRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}",
            DisplayName = "Employee", Position = "Engineering", State = EmployeeState.Active
        };
        db.Employees.Add(employee);
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = Now
        });
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private static ApprovalExecutionService CreateService(RegentDbContext db)
    {
        var clock = new FixedClock();
        var actorId = Guid.NewGuid();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = actorId, Role = "Owner"
        });
        db.SaveChanges();
        return new ApprovalExecutionService(db, new TestTenantContext(TenantId, actorId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock), new TestQuotaGuard(),
            new ApprovalAuthority(db));
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestTenantContext(Guid tenantId, Guid actorId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(actorId);
        public string DisplayName => "Approver";
        public bool IsAuthenticated => true;
    }

    private sealed class TestQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
