using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Realtime;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Memory;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Memory;
using RegentAgent.Performance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ApprovalExecutionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Execution_replays_only_for_the_same_actor_and_hash()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var employeeId = await AddEmployeeAsync(db, tenantId);
        var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
            TargetEmployeeId: employeeId);
        var actorOne = Guid.NewGuid();
        var actorTwo = Guid.NewGuid();
        var first = CreateService(db, tenantId, actorOne);
        var second = CreateService(db, tenantId, actorTwo);

        var executed = await first.TerminateAsync(employeeId, approval.Id.ToString(), "key-1", CancellationToken.None);
        var replay = await first.TerminateAsync(employeeId, approval.Id.ToString(), "key-1", CancellationToken.None);
        Assert.Equal(executed.ApprovalId, replay.ApprovalId);
        Assert.Equal(executed.EmployeeId, replay.EmployeeId);

        await Assert.ThrowsAsync<IdempotencyConflictException>(() => first.OffboardAsync(employeeId,
            approval.Id.ToString(), "key-1", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.TerminateAsync(employeeId,
            approval.Id.ToString(), "key-1", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.TerminateAsync(employeeId,
            approval.Id.ToString(), "key-2", CancellationToken.None));

        Assert.Single(await db.AuditRecords.Where(x => x.EventType == "employee.terminated").ToListAsync());
        Assert.Single(await db.Outbox.ToListAsync());
        Assert.Single(await db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Hire_quota_failure_rolls_back_approval_consumption()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var positionId = await AddPositionAsync(db, tenantId);
        var candidateId = await AddCandidateAsync(db, tenantId, positionId);
        var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringHire,
            TargetCandidateId: candidateId, TargetPositionId: positionId);
        var service = CreateService(db, tenantId, Guid.NewGuid(), rejectQuota: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.HireAsync(candidateId,
            approval.Id.ToString(), "hire-1", CancellationToken.None));

        Assert.Null((await db.Approvals.SingleAsync()).ConsumedAt);
        Assert.Equal("Assessed", (await db.Candidates.SingleAsync()).Status);
        Assert.Equal("Open", (await db.Positions.SingleAsync()).Status);
        Assert.Empty(await db.Employees.ToListAsync());
        Assert.Empty(await db.Outbox.ToListAsync());
        Assert.Empty(await db.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task Relational_hire_quota_failure_rolls_back_consumption_and_partial_mutation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-quota-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            var positionId = await AddPositionAsync(db, tenantId);
            var candidateId = await AddCandidateAsync(db, tenantId, positionId);
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringHire,
                TargetCandidateId: candidateId, TargetPositionId: positionId);
            var service = CreateService(db, tenantId, Guid.NewGuid(), rejectQuota: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.HireAsync(candidateId,
                approval.Id.ToString(), "hire-1", CancellationToken.None));

            Assert.Null((await db.Approvals.AsNoTracking().SingleAsync()).ConsumedAt);
            Assert.Equal("Assessed", (await db.Candidates.AsNoTracking().SingleAsync()).Status);
            Assert.Equal("Open", (await db.Positions.AsNoTracking().SingleAsync()).Status);
            Assert.Empty(await db.Employees.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
            Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Invalid_approval_decider_rolls_back_before_state_mutation(bool selfDecided)
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-decider-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId);
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
                TargetEmployeeId: employeeId);
            approval.DecidedBy = selfDecided ? approval.RequesterUserId : Guid.Empty;
            await db.SaveChangesAsync();
            var service = CreateService(db, tenantId, Guid.NewGuid());

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
                approval.Id.ToString(), "terminate-invalid-decider", CancellationToken.None));

            db.ChangeTracker.Clear();
            Assert.Null((await db.Approvals.AsNoTracking().SingleAsync()).ConsumedAt);
            Assert.Equal(EmployeeState.Active, (await db.Employees.AsNoTracking().SingleAsync()).State);
            Assert.Null((await db.EmploymentPeriods.AsNoTracking().SingleAsync()).EndedAt);
            Assert.Empty(await db.Outbox.ToListAsync());
            Assert.Empty(await db.AuditRecords.ToListAsync());
            Assert.Empty(await db.IdempotencyRecords.ToListAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Termination_failure_before_commit_rolls_back_state_and_leaves_memory_vectors_untouched()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-rollback-{Guid.NewGuid():N}.db");
        try
        {
            var interceptor = new FaultInjectingSaveChangesInterceptor();
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").AddInterceptors(interceptor).Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId);
            var periodId = await db.EmploymentPeriods.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.EndedAt == null).Select(x => x.Id).SingleAsync();
            var projectId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Rollback project", Mission = "Synthetic",
                Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
                TargetAt = Now, UpdatedAt = Now
            });
            db.Tasks.Add(new TaskRecord
            {
                Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = "Rollback task",
                TaskType = "work", Status = "InProgress", Assignee = "Employee",
                AssigneeEmployeeId = employeeId, UpdatedAt = Now
            });
            await db.SaveChangesAsync();
            using var memoryStore = new PersonalMemoryTestStore(new FixedClock());
            var claim = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "Private rollback note",
                Now.AddMinutes(-1), periodId);
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
                TargetEmployeeId: employeeId);
            var service = CreateService(db, tenantId, Guid.NewGuid());
            interceptor.FailOnSaveChanges = true;

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
                approval.Id.ToString(), "terminate-rollback", CancellationToken.None));

            Assert.Equal("Injected failure before commit.", error.Message);
            interceptor.FailOnSaveChanges = false;
            db.ChangeTracker.Clear();
            Assert.Null((await db.Approvals.AsNoTracking().SingleAsync()).ConsumedAt);
            Assert.Equal(EmployeeState.Active, (await db.Employees.AsNoTracking().SingleAsync()).State);
            Assert.Null((await db.EmploymentPeriods.AsNoTracking().SingleAsync()).EndedAt);
            var task = await db.Tasks.AsNoTracking().SingleAsync();
            Assert.Equal("InProgress", task.Status);
            Assert.Equal(1, task.Version);
            Assert.Null(task.LeaseOwner);
            Assert.Null(task.LeaseToken);
            Assert.Null(task.LeaseExpiresAt);
            Assert.Empty(await db.Outbox.ToListAsync());
            Assert.Empty(await db.IdempotencyRecords.ToListAsync());
            Assert.Empty(await db.AuditRecords.ToListAsync());
            Assert.Equal("Active", (await memoryStore.GetClaimAsync(claim.Id))!.Status);
            Assert.NotNull(await memoryStore.GetVectorAsync(tenantId, claim.Id));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Hire_and_rehire_preserve_employee_number_and_use_distinct_approvals()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var positionId = await AddPositionAsync(db, tenantId);
        var candidateId = await AddCandidateAsync(db, tenantId, positionId);
        var hire = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringHire,
            TargetCandidateId: candidateId, TargetPositionId: positionId);
        var service = CreateService(db, tenantId, Guid.NewGuid());

        var hired = await service.HireAsync(candidateId, hire.Id.ToString(), "hire-1", CancellationToken.None);
        var employeeNo = (await db.Employees.SingleAsync()).EmployeeNo;
        var offboard = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeOffboard,
            TargetEmployeeId: hired.EmployeeId);
        await service.OffboardAsync(hired.EmployeeId!.Value, offboard.Id.ToString(), "offboard-1",
            CancellationToken.None);
        var rehire = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringRehire,
            TargetEmployeeId: hired.EmployeeId, TargetPositionId: positionId);

        await service.RehireAsync(hired.EmployeeId.Value, positionId, rehire.Id.ToString(), "rehire-1",
            CancellationToken.None);

        var employee = await db.Employees.SingleAsync();
        Assert.Equal(employeeNo, employee.EmployeeNo);
        Assert.Equal(EmployeeState.Active, employee.State);
        Assert.Equal(2, await db.EmploymentPeriods.CountAsync());
    }

    [Fact]
    public async Task Relational_execution_conditionally_consumes_the_approval_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-execution-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId);
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
                TargetEmployeeId: employeeId);
            var service = CreateService(db, tenantId, Guid.NewGuid());

            await service.TerminateAsync(employeeId, approval.Id.ToString(), "terminate-1", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
                approval.Id.ToString(), "terminate-2", CancellationToken.None));

            Assert.Single(await db.Outbox.ToListAsync());
            Assert.Equal(EmployeeState.Terminated, (await db.Employees.AsNoTracking().SingleAsync()).State);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Relational_hire_serializes_the_tenant_and_fills_the_open_position()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-hire-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenantId = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "hire", CompanyName = "Hire Co" });
            var positionId = await AddPositionAsync(db, tenantId);
            var candidateId = await AddCandidateAsync(db, tenantId, positionId);
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringHire,
                TargetCandidateId: candidateId, TargetPositionId: positionId);
            var service = CreateService(db, tenantId, Guid.NewGuid());

            var result = await service.HireAsync(candidateId, approval.Id.ToString(), "hire-1",
                CancellationToken.None);

            Assert.Equal("Filled", (await db.Positions.SingleAsync()).Status);
            Assert.Equal("Hired", (await db.Candidates.SingleAsync()).Status);
            Assert.Equal(result.EmployeeId, (await db.Employees.SingleAsync()).Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Outbox_handler_failure_retries_with_backoff_then_quarantines_without_inbox()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var clock = new FixedClock();
        var outbox = new OutboxService(db, clock, [new ThrowingHandler()]);
        await outbox.PublishAsync(tenantId, "approval.execution.committed", new { }, CancellationToken.None);

        await outbox.ProcessAsync(CancellationToken.None);

        var message = await db.Outbox.SingleAsync();
        Assert.Equal("Pending", message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.NotNull(message.NextAttemptAt);
        Assert.Equal("InvalidOperationException", message.LastError);
        Assert.Empty(await db.Inbox.ToListAsync());

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            clock.AdvanceTo(message.NextAttemptAt!.Value);
            await outbox.ProcessAsync(CancellationToken.None);
            message = await db.Outbox.SingleAsync();
            Assert.Equal(attempt, message.Attempts);
        }

        Assert.Equal("Quarantined", message.Status);
        Assert.Null(message.NextAttemptAt);
        Assert.Empty(await db.Inbox.ToListAsync());
    }

    [Fact]
    public async Task Forged_approval_execution_message_is_rejected_before_side_effects()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var employeeId = await AddEmployeeAsync(db, tenantId, "Employee");
        var approvalId = Guid.NewGuid();
        var connections = new TenantHubConnectionRegistry();
        var disconnected = false;
        connections.Connected("connection", new TenantHubConnection(Guid.NewGuid(), tenantId, Guid.NewGuid(),
            employeeId.ToString(), () => disconnected = true, Guid.NewGuid()));
        var handler = new ApprovalExecutionOutboxHandler(db, connections, new MemoryService(
            new JsonDocumentStore<MemoryClaim>(Path.Combine(Path.GetTempPath(), $"forged-{Guid.NewGuid():N}.json")),
            new JsonDocumentStore<CorrectionTask>(Path.Combine(Path.GetTempPath(), $"forged-corrections-{Guid.NewGuid():N}.json"))),
            new PerformanceActionService(new JsonDocumentStore<PerformanceActionRecord>(
                Path.Combine(Path.GetTempPath(), $"forged-performance-{Guid.NewGuid():N}.json"))));
        var message = new OutboxRecord
        {
            Id = ApprovalExecutionEvent.DeterministicId(approvalId), TenantId = tenantId,
            EventType = ApprovalExecutionEvent.EventType,
            PayloadJson = JsonSerializer.Serialize(new
            {
                approval_id = approvalId, tenant_id = tenantId, actor_id = Guid.NewGuid(),
                action_type = ApprovalActionTypes.EmployeeTerminate, target_employee_id = employeeId,
                employee_id = employeeId, employment_period_id = Guid.NewGuid(),
                reason = "Forged", occurred_at = Now
            })
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => handler.HandleAsync(message, CancellationToken.None));

        Assert.False(disconnected);
        Assert.Equal(EmployeeState.Active, (await db.Employees.AsNoTracking().SingleAsync()).State);
    }

    [Fact]
    public async Task Poison_message_does_not_block_later_messages()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var outbox = new OutboxService(db, new FixedClock(), [new ThrowingHandler(), new CountingHandler()]);
        await outbox.PublishAsync(tenantId, new ThrowingHandler().EventType, new { }, CancellationToken.None);
        await outbox.PublishAsync(tenantId, new CountingHandler().EventType, new { }, CancellationToken.None);

        await outbox.ProcessAsync(CancellationToken.None);

        Assert.Equal("Pending", (await db.Outbox.SingleAsync(x => x.EventType == new ThrowingHandler().EventType)).Status);
        Assert.Equal("Processed", (await db.Outbox.SingleAsync(x => x.EventType == new CountingHandler().EventType)).Status);
        Assert.Single(await db.Inbox.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_outbox_processing_dispatches_a_message_once()
    {
        var options = new DbContextOptionsBuilder<RegentAgent.ControlPlane.Data.RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var first = new RegentAgent.ControlPlane.Data.RegentDbContext(options);
        await using var second = new RegentAgent.ControlPlane.Data.RegentDbContext(options);
        var handler = new CountingHandler();
        var tenantId = Guid.NewGuid();
        await new OutboxService(first, new FixedClock(), [handler]).PublishAsync(tenantId,
            handler.EventType, new { }, CancellationToken.None);

        await Task.WhenAll(
            new OutboxService(first, new FixedClock(), [handler]).ProcessAsync(CancellationToken.None),
            new OutboxService(second, new FixedClock(), [handler]).ProcessAsync(CancellationToken.None));

        Assert.Equal(1, handler.Count);
        Assert.Single(await second.Inbox.ToListAsync());
    }

    [Fact]
    public async Task Termination_handler_isolates_stable_personal_memory_and_records_projection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"approval-handler-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId, "Employee");
            var periodId = await db.EmploymentPeriods.AsNoTracking().Where(x => x.EmployeeId == employeeId)
                .Select(x => x.Id).SingleAsync();
            var actorId = Guid.NewGuid();
            var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
                TargetEmployeeId: employeeId);
            approval.ConsumedAt = Now;
            approval.ConsumedBy = actorId;
            approval.DecidedBy = Guid.NewGuid();
            approval.DecidedAt = Now.AddMinutes(-1);
            (await db.Employees.SingleAsync(x => x.Id == employeeId)).State = EmployeeState.Terminated;
            (await db.EmploymentPeriods.SingleAsync(x => x.Id == periodId)).EndedAt = Now;
            await db.SaveChangesAsync();
            var newPeriodId = Guid.NewGuid();
            (await db.Employees.SingleAsync(x => x.Id == employeeId)).State = EmployeeState.Active;
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = newPeriodId, TenantId = tenantId, EmployeeId = employeeId,
                PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = Now.AddMinutes(1)
            });
            await db.SaveChangesAsync();
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var corrections = new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json"));
            var personal = new MemoryClaim("personal", tenantId.ToString(), "personal", "Private note", "source:private",
                0.8, "trusted", "Active", 1, Author: "Employee", AuthorEmployeeId: employeeId.ToString());
            var other = new MemoryClaim("other", tenantId.ToString(), "personal", "Other note", "source:other",
                0.8, "trusted", "Active", 1, Author: "Employee", AuthorEmployeeId: Guid.NewGuid().ToString());
            await claims.PutAsync(personal.Id, personal, CancellationToken.None);
            await claims.PutAsync(other.Id, other, CancellationToken.None);
            var memory = new MemoryService(claims, corrections);
            var performance = new PerformanceActionService(
                new JsonDocumentStore<PerformanceActionRecord>(Path.Combine(root, "performance.json")));
            var connections = new TenantHubConnectionRegistry();
            var disconnected = 0;
            var rehiredConnectionDisconnected = 0;
            connections.Connected("connection", new TenantHubConnection(Guid.NewGuid(), tenantId, Guid.NewGuid(),
                employeeId.ToString(), () => disconnected++, periodId));
            connections.Connected("rehired-connection", new TenantHubConnection(Guid.NewGuid(), tenantId, Guid.NewGuid(),
                employeeId.ToString(), () => rehiredConnectionDisconnected++, newPeriodId));
            var handler = new ApprovalExecutionOutboxHandler(db, connections, memory, performance);
            var message = new OutboxRecord
            {
                Id = ApprovalExecutionEvent.DeterministicId(approval.Id), TenantId = tenantId,
                EventType = "approval.execution.committed",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    approval_id = approval.Id, tenant_id = tenantId, actor_id = actorId,
                    action_type = ApprovalActionTypes.EmployeeTerminate, target_employee_id = employeeId,
                    target_candidate_id = (Guid?)null, target_position_id = (Guid?)null,
                    employee_id = employeeId, employment_period_id = periodId,
                    reason = "Terminated after Boss approval", occurred_at = Now
                })
            };

            await handler.HandleAsync(message, CancellationToken.None);

            Assert.Equal(1, disconnected);
            Assert.Equal(0, rehiredConnectionDisconnected);
            Assert.Equal("Isolated", (await claims.GetAsync(personal.Id, CancellationToken.None))!.Status);
            Assert.Equal("Active", (await claims.GetAsync(other.Id, CancellationToken.None))!.Status);
            var projection = Assert.Single(await performance.ListAsync(tenantId.ToString(), CancellationToken.None));
            Assert.Equal("Terminate", projection.ActionType);
            Assert.StartsWith("termination-", projection.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Termination_does_not_interrupt_ambiguous_legacy_display_name_tasks()
    {
        await using var db = CreateDb();
        var tenantId = Guid.NewGuid();
        var employeeId = await AddEmployeeAsync(db, tenantId, "Same Name");
        await AddEmployeeAsync(db, tenantId, "Same Name");
        var stableTaskId = Guid.NewGuid();
        var legacyTaskId = Guid.NewGuid();
        db.Tasks.AddRange(
            new TaskRecord
            {
                Id = stableTaskId, TenantId = tenantId, ProjectId = Guid.NewGuid(), Title = "Stable",
                TaskType = "work", Status = "InProgress", Assignee = "Same Name", AssigneeEmployeeId = employeeId
            },
            new TaskRecord
            {
                Id = legacyTaskId, TenantId = tenantId, ProjectId = Guid.NewGuid(), Title = "Legacy",
                TaskType = "work", Status = "InProgress", Assignee = "Same Name"
            });
        var approval = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeTerminate,
            TargetEmployeeId: employeeId);

        await CreateService(db, tenantId, Guid.NewGuid()).TerminateAsync(employeeId, approval.Id.ToString(),
            "terminate-1", CancellationToken.None);

        Assert.Equal("Interrupted", (await db.Tasks.AsNoTracking().SingleAsync(x => x.Id == stableTaskId)).Status);
        Assert.Equal("InProgress", (await db.Tasks.AsNoTracking().SingleAsync(x => x.Id == legacyTaskId)).Status);
    }

    [Fact]
    public async Task Rehire_does_not_restore_isolated_personal_memory_by_direct_id_or_search()
    {
        var root = Path.Combine(Path.GetTempPath(), $"approval-rehire-memory-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId, "Employee");
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var corrections = new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json"));
            var claim = new MemoryClaim("personal", tenantId.ToString(), "personal", "Private note", "source:private",
                0.8, "trusted", "Active", 1, Author: "Employee", AuthorEmployeeId: employeeId.ToString());
            await claims.PutAsync(claim.Id, claim, CancellationToken.None);
            var memory = new MemoryService(claims, corrections);
            var performance = new PerformanceActionService(
                new JsonDocumentStore<PerformanceActionRecord>(Path.Combine(root, "performance.json")));
            var handler = new ApprovalExecutionOutboxHandler(db, new TenantHubConnectionRegistry(), memory, performance);
            var service = CreateService(db, tenantId, Guid.NewGuid());
            var offboard = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: employeeId);
            await service.OffboardAsync(employeeId, offboard.Id.ToString(), "offboard-1", CancellationToken.None);
            await handler.HandleAsync(await db.Outbox.AsNoTracking().SingleAsync(), CancellationToken.None);

            var positionId = await AddPositionAsync(db, tenantId);
            var rehire = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringRehire,
                TargetEmployeeId: employeeId, TargetPositionId: positionId);
            await service.RehireAsync(employeeId, positionId, rehire.Id.ToString(), "rehire-1", CancellationToken.None);

            Assert.Null(await memory.GetAsync(claim.Id, tenantId.ToString(), "Employee", CancellationToken.None,
                employeeId.ToString()));
            Assert.Empty(await memory.SearchAsync(tenantId.ToString(), "Private note", "Employee",
                CancellationToken.None, employeeId.ToString()));
            Assert.Equal(EmployeeState.Active, (await db.Employees.AsNoTracking().SingleAsync()).State);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Offboarding_defers_memory_isolation_to_outbox_and_spares_rehire_claims()
    {
        var root = Path.Combine(Path.GetTempPath(), $"approval-memory-race-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var employeeId = await AddEmployeeAsync(db, tenantId, "Employee");
            var oldPeriodId = await db.EmploymentPeriods.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.EndedAt == null).Select(x => x.Id).SingleAsync();
            using var memoryStore = new PersonalMemoryTestStore(new FixedClock());
            var oldActive = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "Old active",
                Now.AddMinutes(-1), oldPeriodId);
            var oldQuarantined = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "Old quarantined",
                Now.AddMinutes(-1), oldPeriodId, "Quarantined");
            var service = CreateService(db, tenantId, Guid.NewGuid());
            var offboard = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.EmployeeOffboard,
                TargetEmployeeId: employeeId);

            await service.OffboardAsync(employeeId, offboard.Id.ToString(), "offboard-1", CancellationToken.None);

            Assert.Equal("Active", (await memoryStore.GetClaimAsync(oldActive.Id))!.Status);
            Assert.Equal("Quarantined", (await memoryStore.GetClaimAsync(oldQuarantined.Id))!.Status);
            Assert.NotNull(await memoryStore.GetVectorAsync(tenantId, oldActive.Id));
            Assert.NotNull(await memoryStore.GetVectorAsync(tenantId, oldQuarantined.Id));
            var positionId = await AddPositionAsync(db, tenantId);
            var rehire = await AddApprovalAsync(db, tenantId, ApprovalActionTypes.HiringRehire,
                TargetEmployeeId: employeeId, TargetPositionId: positionId);
            await service.RehireAsync(employeeId, positionId, rehire.Id.ToString(), "rehire-1", CancellationToken.None);
            var newPeriodId = await db.EmploymentPeriods.AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.EndedAt == null).Select(x => x.Id).SingleAsync();
            var newClaim = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "New private note",
                Now.AddMinutes(1), newPeriodId);
            var lateOldEpochClaim = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "Late old epoch",
                Now.AddMinutes(1), oldPeriodId);
            Assert.Null(await memoryStore.Memory.GetAsync(lateOldEpochClaim.Id, tenantId.ToString(), "Employee",
                CancellationToken.None, employeeId.ToString(), viewerEmploymentPeriodId: newPeriodId.ToString()));
            Assert.NotNull(await memoryStore.Memory.GetAsync(newClaim.Id, tenantId.ToString(), "Employee",
                CancellationToken.None, employeeId.ToString(), viewerEmploymentPeriodId: newPeriodId.ToString()));
            var performance = new PerformanceActionService(
                new JsonDocumentStore<PerformanceActionRecord>(Path.Combine(root, "performance.json")));
            var handler = new ApprovalExecutionOutboxHandler(db, new TenantHubConnectionRegistry(),
                memoryStore.Memory, performance);

            await handler.HandleAsync(await db.Outbox.AsNoTracking().SingleAsync(
                message => message.Id == ApprovalExecutionEvent.DeterministicId(offboard.Id)), CancellationToken.None);

            Assert.Equal("Active", (await memoryStore.GetClaimAsync(newClaim.Id))!.Status);
            Assert.Equal("Isolated", (await memoryStore.GetClaimAsync(lateOldEpochClaim.Id))!.Status);
            Assert.Equal("Isolated", (await memoryStore.GetClaimAsync(oldActive.Id))!.Status);
            Assert.Equal("Isolated", (await memoryStore.GetClaimAsync(oldQuarantined.Id))!.Status);
            Assert.NotNull(await memoryStore.GetVectorAsync(tenantId, newClaim.Id));
            Assert.Null(await memoryStore.GetVectorAsync(tenantId, lateOldEpochClaim.Id));
            Assert.Null(await memoryStore.GetVectorAsync(tenantId, oldActive.Id));
            Assert.Null(await memoryStore.GetVectorAsync(tenantId, oldQuarantined.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static ApprovalExecutionService CreateService(RegentDbContext db, Guid tenantId, Guid actorId,
        bool rejectQuota = false)
    {
        var clock = new FixedClock();
        if (!db.Memberships.Any(x => x.TenantId == tenantId && x.UserId == actorId))
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = actorId, Role = "Owner"
            });
        db.SaveChanges();
        return new ApprovalExecutionService(db, new TestTenantContext(tenantId, actorId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock),
            new TestQuotaGuard(rejectQuota), new ApprovalAuthority(db));
    }

    private static async Task<Guid> AddEmployeeAsync(RegentDbContext db, Guid tenantId, string displayName = "Employee")
    {
        var employee = new EmployeeRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{Guid.NewGuid():N}",
            DisplayName = displayName, Position = "Engineering", State = EmployeeState.Active
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

    private static async Task<Guid> AddPositionAsync(RegentDbContext db, Guid tenantId)
    {
        var position = new PositionRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = "Engineering", Responsibilities = "Build",
            Status = "Open", Path = $"root-{Guid.NewGuid():N}"
        };
        db.Positions.Add(position);
        await db.SaveChangesAsync();
        return position.Id;
    }

    private static async Task<Guid> AddCandidateAsync(RegentDbContext db, Guid tenantId, Guid positionId)
    {
        var candidate = new CandidateRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PositionId = positionId, DisplayName = "Candidate",
            ModelVersion = "local", JobDescription = "Build", Status = "Assessed", CreatedAt = Now
        };
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate.Id;
    }

    private static async Task<ApprovalRecord> AddApprovalAsync(RegentDbContext db, Guid tenantId, string actionType,
        Guid? TargetEmployeeId = null, Guid? TargetCandidateId = null, Guid? TargetPositionId = null)
    {
        var binding = new ApprovalBinding(tenantId, actionType, TargetEmployeeId, TargetCandidateId, TargetPositionId);
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = actionType, Title = "Approved action",
            Impact = "Approved", Severity = Severity.High, State = ApprovalState.Approved,
            Requester = "Requester", RequesterUserId = Guid.NewGuid(), TargetEmployeeId = TargetEmployeeId,
            TargetCandidateId = TargetCandidateId, TargetPositionId = TargetPositionId,
            ActionHash = binding.ComputeV2Hash(), DecidedBy = Guid.NewGuid(), DecidedAt = Now.AddMinutes(-2),
            CreatedAt = Now.AddMinutes(-1), ExpiresAt = Now.AddHours(1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();
        return approval;
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

    private sealed class TestQuotaGuard(bool reject) : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            reject ? throw new InvalidOperationException("Plan quota exceeded for employees.")
                : Task.CompletedTask;
    }

    private sealed class ThrowingHandler : IOutboxEventHandler
    {
        public string EventType => "approval.execution.committed";
        public Task HandleAsync(OutboxRecord message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Handler failed.");
    }

    private sealed class CountingHandler : IOutboxEventHandler
    {
        private int count;
        public string EventType => "test.processed";
        public int Count => Volatile.Read(ref count);
        public Task HandleAsync(OutboxRecord message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset current = Now;
        public override DateTimeOffset GetUtcNow() => current;
        public void AdvanceTo(DateTimeOffset value) => current = value;
    }
}
