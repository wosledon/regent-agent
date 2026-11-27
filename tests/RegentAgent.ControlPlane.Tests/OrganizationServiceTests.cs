using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
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
        var position = Assert.Single(await db.Positions.Where(x => x.Name == "Controller").ToListAsync());
        Assert.Equal(PositionPath.Root(position.Id), position.Path);
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
        var parent = positions.Single().Id;
        var expectedPath = PositionPath.Root(parent);
        for (var level = 2; level <= 4; level++)
        {
            var created = await service.CreatePositionAsync(new PositionDto(Guid.Empty, $"Level {level}", "Delivery responsibilities", [], 0, parent, "Open", 1), CancellationToken.None);
            expectedPath = PositionPath.Child(expectedPath, parent, created.Id);
            Assert.Equal(expectedPath, (await db.Positions.SingleAsync(x => x.Id == created.Id)).Path);
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

        var rebased = await db.Positions.SingleAsync(x => x.Id == grandchild.Id);
        Assert.Equal(root.Id, rebased.ParentPositionId);
        Assert.Equal($"{PositionPath.Root(root.Id)}.{grandchild.Id:N}", rebased.Path);
    }

    [Fact]
    public async Task Split_creates_a_sibling_with_the_same_parent_path()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "split-test", CompanyName = "Split Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        await service.StartCompanyAsync(CancellationToken.None);
        var root = (await service.ListPositionsAsync(CancellationToken.None)).Single();
        var child = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Child", "Build", [], 100, root.Id, "Open", 1), CancellationToken.None);

        var split = await service.SplitPositionAsync(child.Id, "Split", "Verify", CancellationToken.None);

        var stored = await db.Positions.SingleAsync(x => x.Id == split.Id);
        Assert.Equal(root.Id, stored.ParentPositionId);
        Assert.Equal($"{PositionPath.Root(root.Id)}.{split.Id:N}", stored.Path);
    }

    [Fact]
    public async Task Sqlite_organization_operations_keep_text_paths_and_loop_fallback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"regent-org-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "sqlite-org", CompanyName = "SQLite Org" });
            await db.SaveChangesAsync();
            var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
            await service.StartCompanyAsync(CancellationToken.None);
            var root = (await service.ListPositionsAsync(CancellationToken.None)).Single();
            var source = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Source", "Build", [], 0, root.Id, "Open", 1), CancellationToken.None);
            var child = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Child", "Test", [], 0, source.Id, "Open", 1), CancellationToken.None);
            var split = await service.SplitPositionAsync(child.Id, "Sibling", "Verify", CancellationToken.None);

            var sourcePath = PositionPath.Child(PositionPath.Root(root.Id), root.Id, source.Id);
            var childPath = PositionPath.Child(sourcePath, source.Id, child.Id);
            Assert.Equal(PositionPath.Sibling(childPath, child.Id, split.Id),
                (await db.Positions.SingleAsync(position => position.Id == split.Id)).Path);
            await service.MergePositionsAsync(source.Id, root.Id, CancellationToken.None);
            Assert.Equal($"{PositionPath.Root(root.Id)}.{child.Id:N}",
                (await db.Positions.SingleAsync(position => position.Id == child.Id)).Path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Merge_rejects_rebasing_a_subtree_beyond_four_levels()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "merge-depth-test", CompanyName = "Merge Depth Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        await service.StartCompanyAsync(CancellationToken.None);
        var root = (await service.ListPositionsAsync(CancellationToken.None)).Single();
        var source = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Source", "Build", [], 0, root.Id, "Open", 1), CancellationToken.None);
        var sourceChild = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Source child", "Build", [], 0, source.Id, "Open", 1), CancellationToken.None);
        await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Source grandchild", "Build", [], 0, sourceChild.Id, "Open", 1), CancellationToken.None);
        var target = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Target", "Build", [], 0, root.Id, "Open", 1), CancellationToken.None);
        var targetChild = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Target child", "Build", [], 0, target.Id, "Open", 1), CancellationToken.None);

        var error = await Record.ExceptionAsync(() => service.MergePositionsAsync(source.Id, targetChild.Id, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(error);
        Assert.Contains("depth", error.Message);
        Assert.Equal("Open", (await db.Positions.SingleAsync(x => x.Id == source.Id)).Status);
    }


    [Fact]
    public async Task Offboarding_releases_the_filled_position_slot()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "offboard-test", CompanyName = "Offboard Test" });
        await db.SaveChangesAsync();
        var positionId = Guid.NewGuid();
        db.Positions.Add(new PositionRecord
        {
            Id = positionId, TenantId = tenantId, Name = "Engineer", Responsibilities = "Build",
            Status = "Filled", Path = $"root-{positionId:N}"
        });
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-9001", DisplayName = "Engineer",
            Position = "Engineer", State = EmployeeState.Active
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = positionId, Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
            Title = "Offboard Engineer", Severity = Severity.High, State = ApprovalState.Approved,
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = employeeId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: employeeId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();

        await CreateExecutionService(db, tenantId).OffboardAsync(employeeId, approval.Id.ToString(),
            "offboard-1", CancellationToken.None);

        Assert.Equal("Open", (await db.Positions.SingleAsync(x => x.Id == positionId)).Status);
        Assert.Equal(EmployeeState.Terminated, (await db.Employees.SingleAsync(x => x.Id == employeeId)).State);
    }

    [Fact]
    public async Task Controller_offboarding_requires_an_active_accepted_successor()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "controller-test", CompanyName = "Controller Test" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var controllerPosition = await db.Positions.SingleAsync(x => x.Name == "Controller");
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
            Title = "Offboard Controller", Severity = Severity.High, State = ApprovalState.Approved,
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = controller.Id,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: controller.Id).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();
        var execution = CreateExecutionService(db, tenantId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.OffboardAsync(controller.Id,
            approval.Id.ToString(), "offboard-1", CancellationToken.None));
        Assert.Equal(EmployeeState.Active, (await db.Employees.SingleAsync(x => x.Id == controller.Id)).State);
        Assert.Null((await db.Approvals.SingleAsync()).ConsumedAt);

        var otherPosition = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Deputy", "Support",
            [], 0, controllerPosition.Id, "Open", 1), CancellationToken.None);
        await AddSuccessorAsync(db, tenantId, controllerPosition.Id, "Inactive Successor", EmployeeState.Terminated);
        await AddSuccessorAsync(db, tenantId, otherPosition.Id, "Misassigned Successor", EmployeeState.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.OffboardAsync(controller.Id,
            approval.Id.ToString(), "offboard-1", CancellationToken.None));
        Assert.Null((await db.Approvals.SingleAsync()).ConsumedAt);

        var successorId = await AddSuccessorAsync(db, tenantId, controllerPosition.Id, "Successor", EmployeeState.Active);

        await execution.OffboardAsync(controller.Id, approval.Id.ToString(), "offboard-1", CancellationToken.None);

        Assert.Equal(EmployeeState.Terminated, (await db.Employees.SingleAsync(x => x.Id == controller.Id)).State);
        Assert.Equal(EmployeeState.Active, (await db.Employees.SingleAsync(x => x.Id == successorId)).State);
    }

    [Fact]
    public async Task Controller_reassignment_requires_an_active_successor()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "reassign", CompanyName = "Reassign Co" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var controllerPosition = await db.Positions.SingleAsync(x => x.Name == "Controller");
        var deputy = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Deputy", "Support",
            [], 0, controllerPosition.Id, "Open", 1), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(
            controller.Id, deputy.Id, CancellationToken.None));

        await AddSuccessorAsync(db, tenantId, controllerPosition.Id, "Successor", EmployeeState.Active);
        await service.AssignAsync(controller.Id, deputy.Id, CancellationToken.None);

        Assert.Equal(deputy.Id, (await db.EmploymentPeriods.AsNoTracking()
            .SingleAsync(x => x.EmployeeId == controller.Id && x.EndedAt == null)).PositionId);
        Assert.True(await db.EmploymentPeriods.AnyAsync(x => x.PositionId == controllerPosition.Id
            && x.EmployeeId != controller.Id && x.EndedAt == null));
    }

    [Fact]
    public async Task Controller_position_cannot_be_merged_away_or_duplicated()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "merge-controller", CompanyName = "Merge Co" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        await service.StartCompanyAsync(CancellationToken.None);
        var controllerPosition = await db.Positions.SingleAsync(x => x.Name == "Controller");
        var target = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Operations", "Coordinate",
            [], 0, null, "Open", 1), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MergePositionsAsync(
            controllerPosition.Id, target.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreatePositionAsync(
            new PositionDto(Guid.Empty, "Controller", "Duplicate", [], 0, null, "Open", 1), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetPositionStatusAsync(
            controllerPosition.Id, "Closed", CancellationToken.None));
        Assert.Equal("Filled", (await db.Positions.AsNoTracking().SingleAsync(x => x.Id == controllerPosition.Id)).Status);

        await AddSuccessorAsync(db, tenantId, controllerPosition.Id, "Successor", EmployeeState.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MergePositionsAsync(
            controllerPosition.Id, target.Id, CancellationToken.None));
        Assert.Equal("Filled", (await db.Positions.AsNoTracking().SingleAsync(x => x.Id == controllerPosition.Id)).Status);
        Assert.Equal("Open", (await db.Positions.AsNoTracking().SingleAsync(x => x.Id == target.Id)).Status);
    }

    [Fact]
    public async Task Controller_identity_uses_the_active_period_position_not_display_name()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "controller-identity", CompanyName = "Identity Co" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new TestQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var controllerPosition = await db.Positions.SingleAsync(x => x.Name == "Controller");
        (await db.Employees.SingleAsync(x => x.Id == controller.Id)).DisplayName = "Operator";
        var deputy = await service.CreatePositionAsync(new PositionDto(Guid.Empty, "Deputy", "Support",
            [], 0, controllerPosition.Id, "Open", 1), CancellationToken.None);
        await AddSuccessorAsync(db, tenantId, deputy.Id, "Controller", EmployeeState.Active);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignAsync(
            controller.Id, deputy.Id, CancellationToken.None));

        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
            Title = "Offboard Operator", Severity = Severity.High, State = ApprovalState.Approved,
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = controller.Id,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: controller.Id).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();
        var execution = CreateExecutionService(db, tenantId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.OffboardAsync(
            controller.Id, approval.Id.ToString(), "offboard-operator", CancellationToken.None));

        await AddSuccessorAsync(db, tenantId, controllerPosition.Id, "Successor", EmployeeState.Active);
        await execution.OffboardAsync(controller.Id, approval.Id.ToString(),
            "offboard-operator", CancellationToken.None);
        Assert.Equal(EmployeeState.Terminated,
            (await db.Employees.AsNoTracking().SingleAsync(x => x.Id == controller.Id)).State);
    }

    [Fact]
    public async Task Rehire_preserves_employee_number_and_does_not_restore_private_memory()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "rehire-test", CompanyName = "Rehire Test" });
        await db.SaveChangesAsync();
        var positionId = Guid.NewGuid();
        db.Positions.Add(new PositionRecord
        {
            Id = positionId, TenantId = tenantId, Name = "Engineer", Responsibilities = "Build",
            Status = "Filled", Path = $"root-{positionId:N}"
        });
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-9003", DisplayName = "Engineer",
            Position = "Engineer", State = EmployeeState.Active
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = positionId, Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        var employeeNo = "EMP-9003";
        var offboard = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
            Title = "Offboard Engineer", Severity = Severity.High, State = ApprovalState.Approved,
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = employeeId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: employeeId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        var rehire = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = ApprovalActionTypes.HiringRehire,
            Title = "Rehire Engineer", Severity = Severity.High, State = ApprovalState.Approved,
            RequesterUserId = Guid.NewGuid(), TargetEmployeeId = employeeId, TargetPositionId = positionId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.HiringRehire,
                TargetEmployeeId: employeeId, TargetPositionId: positionId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.AddRange(offboard, rehire);
        await db.SaveChangesAsync();
        var execution = CreateExecutionService(db, tenantId);
        await execution.OffboardAsync(employeeId, offboard.Id.ToString(), "offboard-1", CancellationToken.None);

        var result = await execution.RehireAsync(employeeId, positionId, rehire.Id.ToString(),
            "rehire-1", CancellationToken.None);
        var rehired = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == result.EmployeeId);

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

    private static async Task<Guid> AddSuccessorAsync(RegentDbContext db, Guid tenantId, Guid positionId,
        string displayName, EmployeeState state)
    {
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}"[..12],
            DisplayName = displayName, Position = "Controller", State = state
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = positionId, Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private static ApprovalExecutionService CreateExecutionService(RegentDbContext db, Guid tenantId)
    {
        var clock = TimeProvider.System;
        var actorId = Guid.NewGuid();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = actorId, Role = "Owner"
        });
        db.SaveChanges();
        return new ApprovalExecutionService(db, new TestTenantContext(tenantId, actorId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock), new TestQuotaGuard(),
            new ApprovalAuthority(db));
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestTenantContext(Guid tenantId, Guid? actorId = null) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(actorId ?? Guid.NewGuid());
        public string DisplayName => "Owner";
        public bool IsAuthenticated => true;
    }
}
