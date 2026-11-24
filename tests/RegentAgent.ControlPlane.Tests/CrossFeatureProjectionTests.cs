using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Application.AgentTools;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class CrossFeatureProjectionTests
{
    [Fact]
    public async Task Approved_plan_projects_runtime_tasks_once_and_syncs_delivery_status()
    {
        var root = Path.Combine(Path.GetTempPath(), $"delivery-projection-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "projection", CompanyName = "Projection Co" });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Projection", Owner = "Controller",
                Stage = ProjectStage.Approved, Health = ProjectHealth.OnTrack, Currency = "CNY",
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var tasks = Enumerable.Range(0, 3).Select(index => new DeliveryTask
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Title = $"Task {index}",
                TaskType = "engineering.implement", Weight = 2, Status = "Ready", Assignee = "Controller"
            }).ToArray();
            var plan = new DecompositionPlan
            {
                Id = Guid.NewGuid(), RequirementId = Guid.NewGuid(), RequirementVersion = 1,
                Status = "Approved", Tasks = tasks
            };
            var store = new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json"));
            await store.PutAsync(projectId.ToString(), new ProjectDeliveryState
            {
                TenantId = tenantId, ProjectId = projectId, Plans = [plan], Tasks = [.. tasks]
            }, CancellationToken.None);
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var delivery = new DeliveryService(store, TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);

            await projection.SyncApprovedPlanAsync(tenantId, projectId, plan, CancellationToken.None);
            await projection.SyncApprovedPlanAsync(tenantId, projectId, plan, CancellationToken.None);
            Assert.Equal(3, await db.Tasks.CountAsync());
            Assert.Single(await db.AuditRecords.Where(x => x.EventType == "delivery.plan.runtime_projected").ToListAsync());

            var taskId = tasks[0].Id;
            await projection.MarkRuntimeClaimedAsync(tenantId, taskId, CancellationToken.None);
            Assert.Equal("Claimed", (await delivery.GetAsync(tenantId, projectId, CancellationToken.None)).Tasks.Single(x => x.Id == taskId).Status);
            await projection.MarkRuntimeCompletedAsync(tenantId, taskId, CancellationToken.None);
            Assert.Equal("ReadyForReview", (await delivery.GetAsync(tenantId, projectId, CancellationToken.None)).Tasks.Single(x => x.Id == taskId).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Application_services_enforce_tenant_quota_before_creating_resources()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "quota", CompanyName = "Quota Co" });
        await db.SaveChangesAsync();
        var quota = new RejectingQuotaGuard();
        var tenant = new TestTenantContext(tenantId);
        var projects = new ProjectService(db, tenant, TimeProvider.System, quota);
        var organization = new OrganizationService(db, tenant, TimeProvider.System, quota);

        await Assert.ThrowsAsync<InvalidOperationException>(() => projects.CreateAsync(
            new CreateProjectDto("Blocked", "Owner", "Quota", 1, 0, "CNY"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => organization.StartCompanyAsync(CancellationToken.None));
        Assert.Empty(await db.Projects.ToListAsync());
        Assert.Empty(await db.Employees.ToListAsync());
    }

    [Fact]
    public async Task Offboarding_interrupts_active_tasks_and_clears_leases()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "offboard", CompanyName = "Offboard Co" });
        await db.SaveChangesAsync();
        var service = new OrganizationService(db, new TestTenantContext(tenantId), TimeProvider.System, new AllowQuotaGuard());
        var controller = await service.StartCompanyAsync(CancellationToken.None);
        var projectId = Guid.NewGuid();
        db.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = "Active", Owner = "Controller",
            Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack, Currency = "CNY",
            TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Tasks.Add(new TaskRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Title = "Active task",
            TaskType = "engineering.implement", Status = "InProgress", Assignee = controller.DisplayName,
            AssigneeEmployeeId = controller.Id, LeaseOwner = "runner", LeaseToken = "lease",
            LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), UpdatedAt = DateTimeOffset.UtcNow
        });
        db.Approvals.Add(new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = "employee.offboard", Title = "Offboard Controller",
            Severity = Severity.High, State = ApprovalState.Approved, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();

        await service.OffboardAsync(controller.Id, (await db.Approvals.SingleAsync()).Id.ToString(), CancellationToken.None);

        var task = await db.Tasks.SingleAsync();
        Assert.Equal("Interrupted", task.Status);
        Assert.Null(task.LeaseOwner);
        Assert.Null(task.LeaseToken);
        Assert.Null(task.LeaseExpiresAt);
    }

    [Fact]
    public async Task Agent_project_tools_project_claim_and_submit_to_runtime_status()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agent-project-projection-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var employeeId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "agent-project", CompanyName = "Agent Project Co" });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Agent Project", Owner = "Controller",
                Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack, Currency = "CNY",
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Tasks.Add(new TaskRecord
            {
                Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = "Agent task",
                TaskType = "engineering.implement", Status = "Pending", Assignee = "Controller", UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var store = new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json"));
            await store.PutAsync(projectId.ToString(), new ProjectDeliveryState
            {
                TenantId = tenantId, ProjectId = projectId,
                Tasks = [new DeliveryTask
                {
                    Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = "Agent task",
                    Status = "Ready", Assignee = "Controller"
                }]
            }, CancellationToken.None);
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var delivery = new DeliveryService(store, TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var projects = new ProjectService(db, new TestTenantContext(tenantId), TimeProvider.System, new AllowQuotaGuard());
            var handler = new ProjectToolHandler(projects, delivery, projection);
            var now = DateTimeOffset.UtcNow;
            var context = new AgentToolContext(new AgentIdentityModel(tenantId, employeeId, taskId, "runner",
                ["tasks:execute"], Guid.NewGuid().ToString("N"), now, now.AddMinutes(15)), "Controller", "{}", "Low", "{}");

            await handler.ExecuteAsync(context, JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "claim_task", projectId, taskId
            })).RootElement, CancellationToken.None);
            Assert.Equal("Assigned", (await db.Tasks.SingleAsync()).Status);
            Assert.Equal("Claimed", (await delivery.GetAsync(tenantId, projectId, CancellationToken.None)).Tasks.Single().Status);

            await handler.ExecuteAsync(context, JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                operation = "submit_task", projectId, taskId
            })).RootElement, CancellationToken.None);
            Assert.Equal("InReview", (await db.Tasks.SingleAsync()).Status);
            Assert.Equal("InReview", (await delivery.GetAsync(tenantId, projectId, CancellationToken.None)).Tasks.Single().Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(Guid.NewGuid());
        public string DisplayName => "Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class RejectingQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Plan quota exceeded.");
    }

    private sealed class AllowQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
