using Microsoft.EntityFrameworkCore;
using RegentAgent.AgentIdentity;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Projects;
using RegentAgent.Contracts.Runtime;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TaskExecutionServiceTests
{
    [Fact]
    public async Task Runner_can_claim_checkpoint_and_complete_task()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-task-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            await audit.BackfillAsync(CancellationToken.None);
            var identityOptions = new AgentIdentityOptions { SigningKey = new string('k', 32), KeyPath = Path.Combine(root, "agent.key") };
            var tokenService = new AgentAssignmentTokenService(new FixedAgentKeyProvider(), identityOptions);
            var delivery = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json")), TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var service = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit, tokenService, projection, new AllowQuotaGuard());
            var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

            var claim = await service.ClaimAsync("runner-test", tenantId, CancellationToken.None);
            Assert.NotNull(claim);
            Assert.Equal("Generate local delivery summary", claim.Title);
            Assert.Equal(tenantId, claim.TenantId);
            Assert.NotEqual(Guid.Empty, claim.EmployeeId);
            Assert.False(string.IsNullOrWhiteSpace(claim.AssignmentToken));
            var identity = new AgentAssignmentTokenValidator(new FixedAgentKeyProvider(), identityOptions).Validate(claim.AssignmentToken);
            Assert.Equal(tenantId, identity.TenantId);
            Assert.Equal(claim.EmployeeId, identity.EmployeeId);
            Assert.Equal(claim.TaskId, identity.TaskId);
            Assert.Contains("infer:employee", identity.Scopes);
            Assert.True(await service.CheckpointAsync(claim.TaskId, new TaskCheckpoint(claim.LeaseToken, "{}"), tenantId, CancellationToken.None));
            Assert.True(await service.CompleteAsync(claim.TaskId,
                new TaskCompletion(claim.LeaseToken, "# Result", "Done", ["test:local"]), tenantId, CancellationToken.None));

            var task = await db.Tasks.SingleAsync(x => x.Id == claim.TaskId);
            Assert.Equal("Done", task.Status);
            Assert.Null(task.LeaseToken);
            Assert.Single(await db.Artifacts.ToListAsync());
            Assert.Contains(await db.AuditRecords.ToListAsync(), x => x.EventType == "task.completed");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Terminated_assignee_tasks_are_not_claimable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-task-terminated-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var employeeId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "terminated", CompanyName = "Terminated Co" });
            db.Employees.Add(new EmployeeRecord
            {
                Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-0001", DisplayName = "Departed",
                Position = "Engineer", State = RegentAgent.Contracts.Enums.EmployeeState.Terminated
            });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Project", Owner = "Departed",
                Stage = RegentAgent.Contracts.Enums.ProjectStage.Active, Health = RegentAgent.Contracts.Enums.ProjectHealth.OnTrack,
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Tasks.Add(new TaskRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Title = "Departed task",
                TaskType = "engineering.implement", Status = "Pending", Assignee = "Departed",
                AssigneeEmployeeId = employeeId, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var options = new AgentIdentityOptions { SigningKey = new string('k', 32), KeyPath = Path.Combine(root, "agent.key") };
            var delivery = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json")), TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var service = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit,
                new AgentAssignmentTokenService(new FixedAgentKeyProvider(), options), projection, new AllowQuotaGuard());

            var claim = await service.ClaimAsync("runner", tenantId, CancellationToken.None);

            Assert.Null(claim);
            Assert.Equal("Pending", (await db.Tasks.SingleAsync()).Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Auto_created_assignee_respects_employee_quota()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-task-quota-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "quota", CompanyName = "Quota Co" });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Project", Owner = "Unknown Runner",
                Stage = RegentAgent.Contracts.Enums.ProjectStage.Active, Health = RegentAgent.Contracts.Enums.ProjectHealth.OnTrack,
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Tasks.Add(new TaskRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Title = "Unknown assignee task",
                TaskType = "engineering.implement", Status = "Pending", Assignee = "Unknown Runner", UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var options = new AgentIdentityOptions { SigningKey = new string('k', 32), KeyPath = Path.Combine(root, "agent.key") };
            var delivery = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json")), TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var service = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit,
                new AgentAssignmentTokenService(new FixedAgentKeyProvider(), options), projection, new RejectingQuotaGuard());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClaimAsync("runner", tenantId, CancellationToken.None));

            Assert.Equal("Pending", (await db.Tasks.SingleAsync()).Status);
            Assert.Empty(await db.Employees.ToListAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Assignee_employee_id_takes_precedence_over_same_display_name()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-task-identity-{Guid.NewGuid():N}");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var activeId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "identity", CompanyName = "Identity Co" });
            db.Employees.AddRange(
                new EmployeeRecord
                {
                    Id = activeId, TenantId = tenantId, EmployeeNo = "EMP-0001", DisplayName = "Same Name",
                    Position = "Engineer", State = RegentAgent.Contracts.Enums.EmployeeState.Active
                },
                new EmployeeRecord
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = "EMP-0002", DisplayName = "Same Name",
                    Position = "Former Engineer", State = RegentAgent.Contracts.Enums.EmployeeState.Terminated
                });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Project", Owner = "Same Name",
                Stage = RegentAgent.Contracts.Enums.ProjectStage.Active, Health = RegentAgent.Contracts.Enums.ProjectHealth.OnTrack,
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            db.Tasks.Add(new TaskRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, Title = "Active identity task",
                TaskType = "engineering.implement", Status = "Pending", Assignee = "Same Name",
                AssigneeEmployeeId = activeId, UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var options = new AgentIdentityOptions { SigningKey = new string('k', 32), KeyPath = Path.Combine(root, "agent.key") };
            var delivery = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json")), TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var service = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit,
                new AgentAssignmentTokenService(new FixedAgentKeyProvider(), options), projection, new AllowQuotaGuard());

            var claim = await service.ClaimAsync("runner", tenantId, CancellationToken.None);

            Assert.NotNull(claim);
            Assert.Equal(activeId, claim.EmployeeId);
            Assert.Equal(activeId, (await db.Tasks.SingleAsync()).AssigneeEmployeeId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class AllowQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RejectingQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Plan quota exceeded.");
    }

    private sealed class FixedAgentKeyProvider : IAgentIdentityKeyProvider
    {
        public byte[] GetSigningKey() => Enumerable.Repeat((byte)'k', 32).ToArray();
    }
}
