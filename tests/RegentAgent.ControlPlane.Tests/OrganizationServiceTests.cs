using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Organization;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class OrganizationServiceTests
{
    [Fact]
    public async Task Company_start_creates_one_controller_and_is_idempotent()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "org-test", CompanyName = "Org Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());

        var first = await service.StartCompanyAsync(CancellationToken.None);
        var second = await service.StartCompanyAsync(CancellationToken.None);

        Assert.Equal("Controller", first.DisplayName);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await db.Positions.Where(x => x.Name == "Controller").ToListAsync());
        Assert.Single(await db.EmploymentPeriods.ToListAsync());
    }

    [Fact]
    public async Task Organization_depth_is_limited_to_four()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "depth-test", CompanyName = "Depth Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        await service.StartCompanyAsync(CancellationToken.None);
        var positions = await service.ListPositionsAsync(CancellationToken.None);
        Guid? parent = positions.Single().Id;
        for (var level = 2; level <= 4; level++)
        {
            var created = await service.CreatePositionAsync(new PositionDto(Guid.Empty, $"Level {level}", "Delivery responsibilities", [], 0, parent, "Open", 1), CancellationToken.None);
            parent = created.Id;
        }

        var error = await Record.ExceptionAsync(() => service.CreatePositionAsync(
            new PositionDto(Guid.Empty, "Level 5", "Delivery responsibilities", [], 0, parent, "Open", 1), CancellationToken.None));

        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("depth", error.Message);
    }

    [Fact]
    public async Task Merge_rejects_descendant_targets_and_preserves_hierarchy()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "merge-test", CompanyName = "Merge Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        await service.StartCompanyAsync(CancellationToken.None);
        var root = (await service.ListPositionsAsync(CancellationToken.None)).Single();
        var child = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Child", "Build", [], 0, root.Id, "Open", 1), CancellationToken.None);
        var grandchild = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Grandchild", "Test", [], 0, child.Id, "Open", 1), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MergePositionsAsync(root.Id, grandchild.Id, CancellationToken.None));
        await service.MergePositionsAsync(child.Id, root.Id, CancellationToken.None);

        Assert.Equal(root.Id, (await db.Positions.SingleAsync(x => x.Id == grandchild.Id)).ParentPositionId);
    }

    [Fact]
    public async Task Offboarding_releases_the_filled_position_slot()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "offboard-test", CompanyName = "Offboard Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = "employee.offboard", Title = "Offboard Controller",
            Severity = Severity.High, State = ApprovalState.Approved, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();

        await service.OffboardAsync(controller.Id, approval.Id.ToString(), CancellationToken.None);

        Assert.Equal("Open", (await db.Positions.SingleAsync(x => x.Name == "Controller")).Status);
        Assert.Equal(EmployeeState.Terminated, (await db.Employees.SingleAsync(x => x.Id == controller.Id)).State);
    }

    [Fact]
    public async Task Rehire_preserves_employee_number_and_does_not_restore_private_memory()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "rehire-test", CompanyName = "Rehire Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var employeeNo = controller.EmployeeNo;
        db.Approvals.Add(new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = "employee.offboard", Title = "Offboard Controller",
            Severity = Severity.High, State = ApprovalState.Approved, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        await service.OffboardAsync(controller.Id, (await db.Approvals.SingleAsync()).Id.ToString(), CancellationToken.None);
        db.Approvals.Add(new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = "hiring.hire", Title = "Rehire Controller",
            Severity = Severity.High, State = ApprovalState.Approved, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
        var positionId = (await db.Positions.SingleAsync()).Id;

        var rehired = await service.RehireAsync(controller.Id, positionId, (await db.Approvals.OrderBy(x => x.CreatedAt).LastAsync()).Id.ToString(), CancellationToken.None);

        Assert.Equal(employeeNo, rehired.EmployeeNo);
        Assert.Equal(EmployeeState.Active, (await db.Employees.SingleAsync()).State);
        Assert.Equal(2, await db.EmploymentPeriods.CountAsync());
    }

    [Fact]
    public async Task Charter_templates_and_candidate_retry_are_available()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "recruit-test", CompanyName = "Recruit Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var templates = await service.ListCharterTemplatesAsync(CancellationToken.None);
        Assert.Contains(templates, template => template.Id == "regulated");
        var position = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Engineer", "Build software", [], 0, null, "Open", 1), CancellationToken.None);
        var candidate = await service.CreateCandidateAsync(position.Id, "Candidate", "local-cheap-v1", "Implement and test job-related features", CancellationToken.None);
        Assert.Equal("Implement and test job-related features", candidate.JobDescription);

        await service.RejectCandidateAsync(candidate.Id, "Insufficient practical evidence", CancellationToken.None);
        var retried = await service.RetryCandidateAsync(candidate.Id, CancellationToken.None);
        Assert.Equal("Screening", retried.Status);
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(Guid.NewGuid());
        public string DisplayName => "Owner";
        public bool IsAuthenticated => true;
    }
}
