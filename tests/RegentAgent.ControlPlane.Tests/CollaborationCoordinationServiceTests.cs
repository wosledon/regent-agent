using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.AgentIdentity;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Projects;
using RegentAgent.Contracts.Runtime;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;
using RegentAgent.Projects;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class CollaborationCoordinationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Delegation_requires_acceptance_and_preserves_delegator_accountability()
    {
        await using var fixture = await Fixture.CreateAsync();
        var delegation = await fixture.Service.CreateDelegationAsync(fixture.Requester,
            NewDelegation(fixture.SourceTaskId, fixture.TargetEmployeeId), null, CancellationToken.None);
        Assert.Equal("Proposed", delegation.Status);
        Assert.Equal(fixture.RequesterEmployeeId, await fixture.OwnerAsync(fixture.SourceTaskId));

        var rejected = await fixture.Service.DecideDelegationAsync(fixture.Target, delegation.Id,
            new CoordinationDecisionRequest(false, "Insufficient context"), null, CancellationToken.None);
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal(fixture.RequesterEmployeeId, await fixture.OwnerAsync(fixture.SourceTaskId));

        var acceptedRequest = NewDelegation(fixture.SourceTaskId, fixture.TargetEmployeeId);
        var accepted = await fixture.Service.CreateDelegationAsync(fixture.Requester, acceptedRequest, null, CancellationToken.None);
        accepted = await fixture.Service.DecideDelegationAsync(fixture.Target, accepted.Id,
            new CoordinationDecisionRequest(true, "Accepted with evidence"), null, CancellationToken.None);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(fixture.TargetEmployeeId, await fixture.OwnerAsync(fixture.SourceTaskId));
        var task = await fixture.Db.Tasks.SingleAsync(x => x.Id == fixture.SourceTaskId);
        Assert.Equal(fixture.RequesterEmployeeId, task.AccountableEmployeeId);
        Assert.Null(task.LeaseToken);

        var claim = await fixture.Execution.ClaimAsync("runner-target", TenantId, CancellationToken.None);
        Assert.NotNull(claim);
        Assert.Equal(fixture.SourceTaskId, claim.TaskId);
        Assert.Equal(fixture.TargetEmployeeId, claim.EmployeeId);
        Assert.Equal("InProgress", (await fixture.Db.Tasks.SingleAsync(x => x.Id == fixture.SourceTaskId)).Status);
        Assert.Equal("InProgress", (await fixture.Service.GetAsync(TenantId, accepted.Id, CancellationToken.None))!.Status);
        Assert.True(await fixture.Execution.CompleteAsync(fixture.SourceTaskId,
            new TaskCompletion(claim.LeaseToken, "done", "Done", ["test:delegation"]), TenantId, CancellationToken.None));
        Assert.Equal("Completed", (await fixture.Service.GetAsync(TenantId, accepted.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Handoff_does_not_transfer_ownership_before_acceptance_and_fences_old_lease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var handoff = await fixture.Service.CreateHandoffAsync(fixture.Requester,
            NewHandoff(fixture.SourceTaskId, fixture.TargetEmployeeId, fixture.SourceLease, fixture.Checkpoint), null, CancellationToken.None);
        Assert.Equal("Requested", handoff.Status);
        Assert.Equal(fixture.RequesterEmployeeId, await fixture.OwnerAsync(fixture.SourceTaskId));

        var accepted = await fixture.Service.DecideHandoffAsync(fixture.Target, handoff.Id,
            new CoordinationDecisionRequest(true, "Checkpoint accepted"), null, CancellationToken.None);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(fixture.TargetEmployeeId, await fixture.OwnerAsync(fixture.SourceTaskId));
        var task = await fixture.Db.Tasks.SingleAsync(x => x.Id == fixture.SourceTaskId);
        Assert.Equal(fixture.TargetEmployeeId, task.AccountableEmployeeId);
        Assert.Equal("Assigned", task.Status);
        Assert.Null(task.LeaseToken);
        Assert.False(await fixture.Execution.CompleteAsync(fixture.SourceTaskId,
            new TaskCompletion(fixture.SourceLease, "stale", "Done", []), TenantId, CancellationToken.None));
    }

    [Fact]
    public async Task Handoff_rejects_artifacts_outside_the_task_scope()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Artifacts.Add(new ArtifactRecord
        {
            Id = Guid.NewGuid(), TenantId = OtherTenantId, ProjectId = fixture.ProjectId,
            TaskId = fixture.SourceTaskId, UploadState = "Ready"
        });
        await fixture.Db.SaveChangesAsync();
        var request = NewHandoff(fixture.SourceTaskId, fixture.TargetEmployeeId, fixture.SourceLease, fixture.Checkpoint) with
        {
            Package = NewHandoff(fixture.SourceTaskId, fixture.TargetEmployeeId, fixture.SourceLease, fixture.Checkpoint).Package with
            {
                ArtifactRefs = [$"artifact:{fixture.Db.Artifacts.Single().Id}"]
            }
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.CreateHandoffAsync(fixture.Requester, request, null, CancellationToken.None));
    }

    [Fact]
    public async Task Interrupt_requires_P0_or_P1_and_rotates_the_lease_on_resume()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.CreateInterruptAsync(fixture.Requester,
            new CreateInterruptRequest(fixture.TargetTaskId, "P2", "Not urgent"), null, CancellationToken.None));

        var interrupt = await fixture.Service.CreateInterruptAsync(fixture.Requester,
            new CreateInterruptRequest(fixture.TargetTaskId, "P1", "Production incident"), null, CancellationToken.None);
        Assert.Equal("Requested", interrupt.Status);
        var suspended = await fixture.Service.AcknowledgeInterruptAsync(fixture.Target, interrupt.Id,
            new InterruptCheckpointRequest(fixture.TargetLease, fixture.Checkpoint), null, CancellationToken.None);
        Assert.Equal("Suspended", suspended.Status);
        Assert.Equal("Interrupted", (await fixture.Db.Tasks.SingleAsync(x => x.Id == fixture.TargetTaskId)).Status);
        Assert.False(await fixture.Execution.CompleteAsync(fixture.TargetTaskId,
            new TaskCompletion(fixture.TargetLease, "stale", "Done", []), TenantId, CancellationToken.None));

        var ready = await fixture.Service.ResolveInterruptAsync(fixture.Requester, interrupt.Id,
            new CoordinationResolutionRequest("Incident contained"), null, CancellationToken.None);
        Assert.Equal("ReadyToResume", ready.Status);
        var resumed = await fixture.Service.ResumeInterruptAsync(fixture.Target, interrupt.Id,
            new InterruptResumeRequest(fixture.TargetLease), null, CancellationToken.None);
        Assert.Equal("Resumed", resumed.Coordination.Status);
        Assert.NotEqual(fixture.TargetLease, resumed.LeaseToken);
        var task = await fixture.Db.Tasks.SingleAsync(x => x.Id == fixture.TargetTaskId);
        Assert.Equal("InProgress", task.Status);
        Assert.Equal(resumed.LeaseToken, task.LeaseToken);
    }

    [Fact]
    public async Task Help_supports_context_roundtrip_redirect_and_resolution()
    {
        await using var fixture = await Fixture.CreateAsync();
        var help = await fixture.Service.CreateHelpAsync(fixture.Requester, new CreateHelpRequest(
            fixture.SourceTaskId, fixture.TargetEmployeeId, "Which evidence is missing?",
            ["Ran local tests"], ["test:local"], "A concrete evidence list", DateTimeOffset.UtcNow.AddHours(2)),
            null, CancellationToken.None);
        Assert.Equal("Open", help.Status);
        help = await fixture.Service.RespondHelpAsync(fixture.Target, help.Id,
            new HelpResponseRequest("NeedMoreContext", "Need the failing assertion"), null, CancellationToken.None);
        Assert.Equal("NeedMoreContext", help.Status);
        help = await fixture.Service.AddHelpContextAsync(fixture.Requester, help.Id,
            new HelpContextRequest("Assertion compares the audit hash", ["test:audit"]), null, CancellationToken.None);
        Assert.Equal("Open", help.Status);
        help = await fixture.Service.RespondHelpAsync(fixture.Target, help.Id,
            new HelpResponseRequest("Answer", "Attach the audit verification result"), null, CancellationToken.None);
        Assert.Equal("Answered", help.Status);
        help = await fixture.Service.ResolveHelpAsync(fixture.Requester, help.Id,
            "Audit verification attached", null, CancellationToken.None);
        Assert.Equal("Resolved", help.Status);
        Assert.NotNull(help.CompletedAt);
    }

    [Fact]
    public async Task Idempotency_and_cursor_pagination_are_stable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = new CreateHelpRequest(fixture.SourceTaskId, fixture.TargetEmployeeId, "Question one",
            ["Attempted"], [], "Answer", DateTimeOffset.UtcNow.AddHours(1));
        var first = await fixture.Service.CreateHelpAsync(fixture.Requester, request, "same-key", CancellationToken.None);
        var repeated = await fixture.Service.CreateHelpAsync(fixture.Requester, request, "same-key", CancellationToken.None);
        Assert.Equal(first.Id, repeated.Id);
        await fixture.Service.CreateHelpAsync(fixture.Requester,
            request with { Question = "Question two" }, "second-key", CancellationToken.None);

        var page1 = await fixture.Service.ListAsync(TenantId,
            new CoordinationPageQuery { Limit = 1, Kind = "Help" }, CancellationToken.None);
        Assert.Single(page1.Items);
        Assert.NotNull(page1.NextCursor);
        var page2 = await fixture.Service.ListAsync(TenantId,
            new CoordinationPageQuery { Limit = 1, Kind = "Help", Cursor = page1.NextCursor }, CancellationToken.None);
        Assert.Single(page2.Items);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
        Assert.Null(page2.NextCursor);
    }

    [Fact]
    public async Task Cross_tenant_targets_and_requests_are_not_visible()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.CreateDelegationAsync(fixture.Requester,
            NewDelegation(fixture.SourceTaskId, fixture.OtherTenantEmployeeId), null, CancellationToken.None));
        var help = await fixture.Service.CreateHelpAsync(fixture.Requester, new CreateHelpRequest(
            fixture.SourceTaskId, fixture.TargetEmployeeId, "Private question", ["Attempted"], [], "Answer",
            DateTimeOffset.UtcNow.AddHours(1)), null, CancellationToken.None);
        Assert.Null(await fixture.Service.GetAsync(OtherTenantId, help.Id, CancellationToken.None));
    }

    private static CreateTaskDelegationRequest NewDelegation(Guid taskId, Guid targetId) => new(
        taskId, targetId, 3, "Evidence accepted", ["test:pass"], [], "Workload balancing");

    private static CreateHandoffRequest NewHandoff(Guid taskId, Guid targetId, string leaseToken, string checkpoint) => new(
        taskId, targetId, leaseToken, new HandoffPackageDto(
            "Finish the task", "All checks pass", checkpoint, ["Captured checkpoint"], ["Run verification"],
            [], [], ["Provider timeout"], [], "Run verification and attach evidence"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"regent-coordination-{Guid.NewGuid():N}");
        public required RegentDbContext Db { get; init; }
        public required CollaborationCoordinationService Service { get; init; }
        public required TaskExecutionService Execution { get; init; }
        public required AgentIdentityModel Requester { get; init; }
        public required AgentIdentityModel Target { get; init; }
        public required Guid ProjectId { get; init; }
        public required Guid SourceTaskId { get; init; }
        public required Guid TargetTaskId { get; init; }
        public required Guid RequesterEmployeeId { get; init; }
        public required Guid TargetEmployeeId { get; init; }
        public required Guid OtherTenantEmployeeId { get; init; }
        public required string SourceLease { get; init; }
        public required string TargetLease { get; init; }
        public required string Checkpoint { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"regent-coordination-{Guid.NewGuid():N}");
            var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var tenantId = TenantId;
            var requesterId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var sourceTaskId = Guid.NewGuid();
            var targetTaskId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            const string checkpoint = "{\"step\":1,\"safe\":true}";
            const string sourceLease = "source-lease";
            const string targetLease = "target-lease";
            db.Tenants.Add(new TenantRecord { Id = tenantId });
            db.Tenants.Add(new TenantRecord { Id = OtherTenantId });
            db.Employees.AddRange(
                new EmployeeRecord { Id = requesterId, TenantId = tenantId, EmployeeNo = "EMP-R", DisplayName = "Requester", State = EmployeeState.Active },
                new EmployeeRecord { Id = targetId, TenantId = tenantId, EmployeeNo = "EMP-T", DisplayName = "Target", State = EmployeeState.Active },
                new EmployeeRecord { Id = Guid.NewGuid(), TenantId = OtherTenantId, EmployeeNo = "EMP-O", DisplayName = "Outsider", State = EmployeeState.Active });
            db.Projects.Add(new ProjectRecord { Id = projectId, TenantId = tenantId, Name = "Coordination" });
            db.Tasks.AddRange(
                new TaskRecord
                {
                    Id = sourceTaskId, TenantId = tenantId, ProjectId = projectId, Title = "Source", Status = "InProgress",
                    Assignee = "Requester", AssigneeEmployeeId = requesterId, AccountableEmployeeId = requesterId,
                    LeaseToken = sourceLease, LeaseExpiresAt = now.AddHours(1), CheckpointJson = checkpoint, UpdatedAt = now
                },
                new TaskRecord
                {
                    Id = targetTaskId, TenantId = tenantId, ProjectId = projectId, Title = "Target", Status = "InProgress",
                    Assignee = "Target", AssigneeEmployeeId = targetId, AccountableEmployeeId = targetId,
                    LeaseToken = targetLease, LeaseExpiresAt = now.AddHours(1), CheckpointJson = checkpoint, UpdatedAt = now
                });
            await db.SaveChangesAsync();
            var audit = new AuditIntegrityService(db, TimeProvider.System);
            var options = new CollaborationOptions { DefaultWipLimit = 10 };
            var service = new CollaborationCoordinationService(db, TimeProvider.System, audit,
                new IdempotencyService(db, TimeProvider.System), options, new CursorCodec("test-cursor-key"));
            var identityOptions = new AgentIdentityOptions { SigningKey = new string('k', 32), KeyPath = Path.Combine(root, "agent.key") };
            var tokens = new AgentAssignmentTokenService(new FixedAgentKeyProvider(), identityOptions);
            var delivery = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(Path.Combine(root, "delivery.json")), TimeProvider.System);
            var projection = new DeliveryRuntimeProjectionService(db, delivery, audit, TimeProvider.System);
            var execution = new TaskExecutionService(db, new LocalFileObjectStore(root), TimeProvider.System, audit, tokens, projection, new AllowQuotaGuard());
            AgentIdentityModel Identity(Guid employeeId, Guid taskId) => new(tenantId, employeeId, taskId, "runner",
                ["tasks:execute"], Guid.NewGuid().ToString("N"), now, now.AddHours(1));
            var outsiderId = db.Employees.Single(x => x.TenantId == OtherTenantId).Id;
            return new Fixture
            {
                Db = db, Service = service, Execution = execution, ProjectId = projectId,
                SourceTaskId = sourceTaskId, TargetTaskId = targetTaskId, RequesterEmployeeId = requesterId,
                TargetEmployeeId = targetId, OtherTenantEmployeeId = outsiderId, SourceLease = sourceLease,
                TargetLease = targetLease, Checkpoint = checkpoint, Requester = Identity(requesterId, sourceTaskId),
                Target = Identity(targetId, targetTaskId)
            };
        }

        public Task<Guid> OwnerAsync(Guid taskId) =>
            Db.Tasks.AsNoTracking().Where(x => x.Id == taskId).Select(x => x.AssigneeEmployeeId ?? Guid.Empty).SingleAsync();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class AllowQuotaGuard : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedAgentKeyProvider : IAgentIdentityKeyProvider
    {
        public byte[] GetSigningKey() => Enumerable.Repeat((byte)'k', 32).ToArray();
    }
}
