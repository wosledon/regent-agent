using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("Postgres runtime")]
public sealed class PostgresApprovalExecutionTests
{
    [PostgresFact]
    public async Task Concurrent_consumption_of_one_approval_commits_one_execution()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        await SeedTerminationAsync(connectionString, tenantId, actorId, employeeId, approvalId);
        try
        {
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<ExecutionAttempt> Start(string key)
            {
                await barrier.Task;
                return await ExecuteAsync(connectionString, tenantId, actorId, employeeId, approvalId, key);
            }
            var first = Start("first");
            var second = Start("second");
            barrier.SetResult();
            var results = await Task.WhenAll(first, second);

            Assert.Equal(1, results.Count(result => result.Succeeded));
            var conflict = Assert.Single(results, result => !result.Succeeded);
            Assert.Contains("already been consumed", conflict.Error, StringComparison.Ordinal);
            await using var db = CreateDb(connectionString, tenantId);
            Assert.Equal(1, await db.Outbox.CountAsync());
            Assert.Single(await db.AuditRecords.Where(x => x.EventType == "employee.terminated").ToListAsync());
            Assert.Equal(EmployeeState.Terminated, (await db.Employees.AsNoTracking().SingleAsync()).State);
            var consumed = await db.Approvals.AsNoTracking().SingleAsync();
            Assert.NotNull(consumed.ConsumedAt);
            Assert.Equal(actorId, consumed.ConsumedBy);
        }
        finally { await CleanupAsync(tenantId, actorId); }
    }

    [PostgresFact]
    public async Task Concurrent_approvals_serialize_audit_chain_appends()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var firstEmployee = Guid.NewGuid();
        var secondEmployee = Guid.NewGuid();
        var firstApproval = Guid.NewGuid();
        var secondApproval = Guid.NewGuid();
        await SeedTerminationAsync(connectionString, tenantId, actorId, firstEmployee, firstApproval);
        await SeedSecondTerminationAsync(connectionString, tenantId, secondEmployee, secondApproval);
        try
        {
            var results = await Task.WhenAll(
                ExecuteAsync(connectionString, tenantId, actorId, firstEmployee, firstApproval, "first"),
                ExecuteAsync(connectionString, tenantId, actorId, secondEmployee, secondApproval, "second"));

            Assert.All(results, result => Assert.True(result.Succeeded, result.Error));
            await using var db = CreateDb(connectionString, tenantId);
            var audits = await db.AuditRecords.Where(x => x.EventType == "employee.terminated")
                .OrderBy(x => x.Seq).ToListAsync();
            Assert.Equal(new long[] { 1, 2 }, audits.Select(x => x.Seq));
            Assert.True((await new AuditIntegrityService(db, TimeProvider.System).VerifyAsync(tenantId, CancellationToken.None)).Valid);
            Assert.Equal(2, await db.Outbox.CountAsync());
        }
        finally { await CleanupAsync(tenantId, actorId); }
    }

    [PostgresFact]
    public async Task Hire_quota_failure_rolls_back_consumption_and_partial_mutation()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        await SeedHireAsync(connectionString, tenantId, actorId, positionId, candidateId, approvalId);
        try
        {
            await using var db = CreateDb(connectionString, tenantId);
            var service = CreateService(db, tenantId, actorId, rejectQuota: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.HireAsync(candidateId,
                approvalId.ToString(), "hire", CancellationToken.None));

            Assert.Null((await db.Approvals.AsNoTracking().SingleAsync()).ConsumedAt);
            Assert.Equal("Assessed", (await db.Candidates.AsNoTracking().SingleAsync()).Status);
            Assert.Equal("Open", (await db.Positions.AsNoTracking().SingleAsync()).Status);
            Assert.Empty(await db.Employees.ToListAsync());
            Assert.Empty(await db.Outbox.ToListAsync());
        }
        finally { await CleanupAsync(tenantId, actorId); }
    }

    [PostgresFact]
    public async Task Termination_failure_before_commit_rolls_back_state_and_leaves_memory_vectors_untouched()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        await SeedTerminationAsync(connectionString, tenantId, actorId, employeeId, approvalId);
        try
        {
            var periodId = Guid.Empty;
            var projectId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            await using (var seed = CreateDb(connectionString, tenantId))
            {
                periodId = await seed.EmploymentPeriods.AsNoTracking()
                    .Where(x => x.EmployeeId == employeeId && x.EndedAt == null).Select(x => x.Id).SingleAsync();
                seed.Projects.Add(new ProjectRecord
                {
                    Id = projectId, TenantId = tenantId, Name = "Rollback project", Mission = "Synthetic",
                    Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
                    TargetAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                });
                seed.Tasks.Add(new TaskRecord
                {
                    Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = "Rollback task",
                    TaskType = "work", Status = "InProgress", Assignee = "Employee",
                    AssigneeEmployeeId = employeeId, UpdatedAt = DateTimeOffset.UtcNow
                });
                await seed.SaveChangesAsync();
            }
            using var memoryStore = new PersonalMemoryTestStore(TimeProvider.System);
            var claim = await memoryStore.SeedPersonalClaimAsync(tenantId, employeeId, "Private rollback note",
                DateTimeOffset.UtcNow, periodId);
            var interceptor = new FaultInjectingSaveChangesInterceptor();
            await using (var db = CreateDb(connectionString, tenantId, interceptor))
            {
                var service = CreateService(db, tenantId, actorId);
                interceptor.FailOnSaveChanges = true;

                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.TerminateAsync(employeeId,
                    approvalId.ToString(), "terminate-rollback", CancellationToken.None));

                Assert.Equal("Injected failure before commit.", error.Message);
            }

            await using var verification = CreateDb(connectionString, tenantId);
            Assert.Null((await verification.Approvals.AsNoTracking().SingleAsync()).ConsumedAt);
            Assert.Equal(EmployeeState.Active, (await verification.Employees.AsNoTracking().SingleAsync()).State);
            Assert.Null((await verification.EmploymentPeriods.AsNoTracking().SingleAsync()).EndedAt);
            var task = await verification.Tasks.AsNoTracking().SingleAsync();
            Assert.Equal("InProgress", task.Status);
            Assert.Equal(1, task.Version);
            Assert.Null(task.LeaseOwner);
            Assert.Null(task.LeaseToken);
            Assert.Null(task.LeaseExpiresAt);
            Assert.Empty(await verification.Outbox.ToListAsync());
            Assert.Empty(await verification.IdempotencyRecords.ToListAsync());
            Assert.Empty(await verification.AuditRecords.ToListAsync());
            Assert.Equal("Active", (await memoryStore.GetClaimAsync(claim.Id))!.Status);
            Assert.NotNull(await memoryStore.GetVectorAsync(tenantId, claim.Id));
        }
        finally { await CleanupAsync(tenantId, actorId); }
    }

    private static async Task<ExecutionAttempt> ExecuteAsync(string connectionString, Guid tenantId, Guid actorId,
        Guid employeeId, Guid approvalId, string key)
    {
        try
        {
            await using var db = CreateDb(connectionString, tenantId);
            await CreateService(db, tenantId, actorId).TerminateAsync(employeeId, approvalId.ToString(), key,
                CancellationToken.None);
            return new ExecutionAttempt(true, null);
        }
        catch (InvalidOperationException exception)
        {
            return new ExecutionAttempt(false, exception.Message);
        }
    }

    private sealed record ExecutionAttempt(bool Succeeded, string? Error);

    private static ApprovalExecutionService CreateService(RegentDbContext db, Guid tenantId, Guid actorId,
        bool rejectQuota = false)
    {
        var clock = TimeProvider.System;
        return new ApprovalExecutionService(db, new TestTenantContext(tenantId, actorId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock),
            new TestQuotaGuard(rejectQuota), new ApprovalAuthority(db));
    }

    private static RegentDbContext CreateDb(string connectionString, Guid tenantId,
        SaveChangesInterceptor? interceptor = null)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", tenantId.ToString())], "test"))
            }
        };
        var options = new DbContextOptionsBuilder<RegentDbContext>()
            .UseNpgsql(connectionString).AddInterceptors(new TenantRlsConnectionInterceptor(accessor));
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new RegentDbContext(options.Options);
    }

    private static async Task SeedTerminationAsync(string connectionString, Guid tenantId, Guid actorId,
        Guid employeeId, Guid approvalId)
    {
        await using var db = CreateDb(connectionString, tenantId);
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "approval-pg", CompanyName = "Approval PG" });
        db.Users.Add(new UserRecord { Id = actorId, Email = $"{actorId:N}@example.test", DisplayName = "Owner" });
        db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenantId, UserId = actorId, Role = "Owner" });
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"EMP-{employeeId:N}", DisplayName = "Employee",
            Position = "Engineering", State = EmployeeState.Active
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        db.Approvals.Add(new ApprovalRecord
        {
            Id = approvalId, TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeTerminate,
            Title = "Terminate", Impact = "Ends employment", Severity = Severity.High,
            State = ApprovalState.Approved, Requester = "Requester", RequesterUserId = Guid.NewGuid(),
            TargetEmployeeId = employeeId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeTerminate, employeeId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSecondTerminationAsync(string connectionString, Guid tenantId,
        Guid employeeId, Guid approvalId)
    {
        await using var db = CreateDb(connectionString, tenantId);
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"EMP-{employeeId:N}", DisplayName = "Employee 2",
            Position = "Engineering", State = EmployeeState.Active
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        db.Approvals.Add(new ApprovalRecord
        {
            Id = approvalId, TenantId = tenantId, ActionType = ApprovalActionTypes.EmployeeTerminate,
            Title = "Terminate 2", Impact = "Ends employment", Severity = Severity.High,
            State = ApprovalState.Approved, Requester = "Requester", RequesterUserId = Guid.NewGuid(),
            TargetEmployeeId = employeeId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.EmployeeTerminate, employeeId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedHireAsync(string connectionString, Guid tenantId, Guid actorId,
        Guid positionId, Guid candidateId, Guid approvalId)
    {
        await using var db = CreateDb(connectionString, tenantId);
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "hire-pg", CompanyName = "Hire PG" });
        db.Users.Add(new UserRecord { Id = actorId, Email = $"{actorId:N}@example.test", DisplayName = "Owner" });
        db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenantId, UserId = actorId, Role = "Owner" });
        db.Positions.Add(new PositionRecord
        {
            Id = positionId, TenantId = tenantId, Name = "Engineer", Responsibilities = "Build",
            Status = "Open", Path = PositionPath.Root(positionId)
        });
        db.Candidates.Add(new CandidateRecord
        {
            Id = candidateId, TenantId = tenantId, PositionId = positionId, DisplayName = "Candidate",
            ModelVersion = "local", JobDescription = "Build", Status = "Assessed", CreatedAt = DateTimeOffset.UtcNow
        });
        db.Approvals.Add(new ApprovalRecord
        {
            Id = approvalId, TenantId = tenantId, ActionType = ApprovalActionTypes.HiringHire,
            Title = "Hire", Impact = "Adds employee", Severity = Severity.High,
            State = ApprovalState.Approved, Requester = "Requester", RequesterUserId = Guid.NewGuid(),
            TargetCandidateId = candidateId, TargetPositionId = positionId,
            ActionHash = new ApprovalBinding(tenantId, ApprovalActionTypes.HiringHire, null, candidateId, positionId).ComputeV2Hash(),
            DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task CleanupAsync(Guid tenantId, Guid actorId)
    {
        var cleanupConnectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")
            ?? throw new InvalidOperationException("REGENT_TEST_POSTGRES_MIGRATION_DSN is required for audit cleanup.");
        await using var connection = new NpgsqlConnection(cleanupConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT set_config('app.tenant_id', @tenant_id::text, true);
                DELETE FROM "IdempotencyRecords" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Inbox" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Outbox" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "AuditRecords" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Notifications" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Tasks" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Projects" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Approvals" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "EmploymentPeriods" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Employees" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Candidates" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Positions" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Memberships" WHERE "TenantId" = @tenant_id::uuid;
                DELETE FROM "Tenants" WHERE "Id" = @tenant_id::uuid;
                DELETE FROM "Users" WHERE "Id" = @actor_id::uuid;
                """;
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("actor_id", actorId);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private sealed class TestTenantContext(Guid tenantId, Guid actorId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(actorId);
        public string DisplayName => "Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class TestQuotaGuard(bool reject) : ITenantQuotaGuard
    {
        public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
            reject ? throw new InvalidOperationException("Quota exceeded.") : Task.CompletedTask;
    }
}
