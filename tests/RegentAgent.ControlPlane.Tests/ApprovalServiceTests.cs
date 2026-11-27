using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Api;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ApprovalServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Create_uses_authenticated_display_name_instead_of_client_requester()
    {
        await using var db = CreateDb();
        var clock = new FixedClock();
        var service = CreateService(db, clock, "Authenticated Requester");

        var approval = await service.CreateAsync(Request("Forged Requester"), CancellationToken.None);

        Assert.Equal("Authenticated Requester", approval.Requester);
        Assert.Equal("Authenticated Requester", (await db.Approvals.SingleAsync()).Requester);
    }

    [Fact]
    public async Task Requester_cannot_self_approve_even_when_an_independent_decision_can_replay()
    {
        await using var db = CreateDb();
        var clock = new FixedClock();
        var requester = CreateService(db, clock, "Requester");
        var independent = CreateService(db, clock, "Independent Approver");
        var created = await requester.CreateAsync(Request("Forged"), CancellationToken.None);
        var id = new ApprovalId(created.Id);
        const string key = "approval-decision-1";

        var decided = await independent.DecideAsync(id, new ApprovalDecision("approve", "Evidence checked"), CancellationToken.None, key);
        Assert.True(decided.IsSuccess);
        Assert.Equal(ApprovalState.Approved, decided.Value!.State);

        var selfDecision = await requester.DecideAsync(id, new ApprovalDecision("reject", "Self approval"), CancellationToken.None, key);
        Assert.False(selfDecision.IsSuccess);
        Assert.Equal("FORBIDDEN", selfDecision.Error!.Code);

        var conflict = await independent.DecideAsync(id, new ApprovalDecision("reject", "Different replay body"),
            CancellationToken.None, key);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.Error!.Code);

        var replay = await independent.DecideAsync(id, new ApprovalDecision("approve", "Evidence checked"),
            CancellationToken.None, key);
        Assert.True(replay.IsSuccess);
        Assert.Equal(ApprovalState.Approved, replay.Value!.State);
        Assert.Single(await db.AuditRecords.Where(x => x.EventType == "approval.decided").ToListAsync());
    }

    [Fact]
    public async Task Requester_identity_survives_display_name_changes()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var requester = CreateService(db, new FixedClock(), "Before", userId);
        var renamedRequester = CreateService(db, new FixedClock(), "After", userId);
        var created = await requester.CreateAsync(Request("Forged"), CancellationToken.None);

        var result = await renamedRequester.DecideAsync(new ApprovalId(created.Id),
            new ApprovalDecision("approve", "Self approval"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.Error!.Code);
    }

    [Fact]
    public async Task Distinct_users_with_the_same_display_name_are_not_the_requester()
    {
        await using var db = CreateDb();
        var requester = CreateService(db, new FixedClock(), "Shared name", Guid.NewGuid());
        var approver = CreateService(db, new FixedClock(), "Shared name", Guid.NewGuid());
        var created = await requester.CreateAsync(Request("Forged"), CancellationToken.None);

        var result = await approver.DecideAsync(new ApprovalId(created.Id),
            new ApprovalDecision("approve", "Independent"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Create_binds_v2_hash_to_tenant_and_stable_target_ids()
    {
        await using var db = CreateDb();
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = TenantId, EmployeeNo = "EMP-0001", DisplayName = "Employee",
            Position = "Engineering", State = EmployeeState.Active
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FixedClock(), "Requester");
        var request = new CreateApprovalDto(ApprovalActionTypes.EmployeeOffboard, "Title must not bind the target",
            "Ends employment", Severity.High, "Forged", 60, ["evidence:test"], TargetEmployeeId: employeeId);

        var created = await service.CreateAsync(request, CancellationToken.None);

        var stored = await db.Approvals.SingleAsync();
        var binding = new ApprovalBinding(TenantId, ApprovalActionTypes.EmployeeOffboard, TargetEmployeeId: employeeId);
        Assert.Equal(binding.ComputeV2Hash(), stored.ActionHash);
        Assert.Equal(employeeId, stored.TargetEmployeeId);
        var audit = await db.AuditRecords.SingleAsync(x => x.EventType == "approval.created");
        Assert.Contains(employeeId.ToString(), audit.PayloadJson);
        Assert.Contains(stored.ActionHash, audit.PayloadJson);
    }

    [Fact]
    public async Task Decide_rejects_empty_actor_and_empty_requester_before_replay()
    {
        await using var db = CreateDb();
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ActionType = "release.execute", Title = "Release",
            Impact = "Deploy", Severity = Severity.High, Requester = "Legacy", RequesterUserId = Guid.Empty,
            CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.Approvals.Add(approval);
        var actorId = Guid.NewGuid();
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Key = "decision-key",
            Operation = $"approval-decision:{approval.Id}:{actorId}", RequestHash = "hash",
            Status = "Completed", ResponseJson = "null"
        });
        await db.SaveChangesAsync();
        var service = CreateService(db, new FixedClock(), "Approver", actorId);

        var result = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("approve", "ok"),
            CancellationToken.None, "decision-key");

        Assert.False(result.IsSuccess);
        Assert.Equal("FORBIDDEN", result.Error!.Code);
    }

    [Fact]
    public async Task Decision_idempotency_is_actor_scoped()
    {
        await using var db = CreateDb();
        var requester = CreateService(db, new FixedClock(), "Requester", Guid.NewGuid());
        var firstApprover = CreateService(db, new FixedClock(), "First", Guid.NewGuid());
        var secondApprover = CreateService(db, new FixedClock(), "Second", Guid.NewGuid());
        var created = await requester.CreateAsync(Request("Forged"), CancellationToken.None);
        var id = new ApprovalId(created.Id);

        var first = await firstApprover.DecideAsync(id, new ApprovalDecision("approve", "checked"),
            CancellationToken.None, "shared-key");
        var second = await secondApprover.DecideAsync(id, new ApprovalDecision("approve", "checked"),
            CancellationToken.None, "shared-key");

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal("INVALID_STATE", second.Error!.Code);
    }

    [Fact]
    public async Task Relational_decision_uses_conditional_pending_and_unexpired_update()
    {
        var path = Path.Combine(Path.GetTempPath(), $"approval-decision-{Guid.NewGuid():N}.db");
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            await db.Database.EnsureCreatedAsync();
            var clock = new FixedClock();
            var approval = new ApprovalRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, ActionType = "release.execute", Title = "Release",
                Impact = "Deploy", Severity = Severity.High, Requester = "Requester",
                RequesterUserId = Guid.NewGuid(), CreatedAt = clock.GetUtcNow().AddMinutes(-1),
                ExpiresAt = clock.GetUtcNow().AddHours(1)
            };
            db.Approvals.Add(approval);
            await db.SaveChangesAsync();
            var service = CreateService(db, clock, "Approver", Guid.NewGuid());

            var result = await service.DecideAsync(new ApprovalId(approval.Id),
                new ApprovalDecision("approve", "checked"), CancellationToken.None, "decision-1");

            Assert.True(result.IsSuccess);
            Assert.Equal(ApprovalState.Approved, result.Value!.State);
            Assert.Single(await db.AuditRecords.Where(x => x.EventType == "approval.decided").ToListAsync());
            Assert.Single(await db.Notifications.ToListAsync());
            Assert.Single(await db.IdempotencyRecords.ToListAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("/v1/approvals/{0}/decision")]
    [InlineData("/v1/APPROVALS/{0}/DECISION/")]
    public async Task Approval_decision_http_middleware_does_not_short_circuit_service_replay_checks(string pathTemplate)
    {
        await using var db = CreateDb();
        var approvalId = Guid.NewGuid();
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Key = "decision-key",
            Operation = $"POST:/v1/approvals/{approvalId}/decision", RequestHash = "hash",
            Status = "Completed", StatusCode = 200, ResponseJson = Convert.ToBase64String(Encoding.UTF8.GetBytes("replayed"))
        });
        await db.SaveChangesAsync();
        var nextCalled = false;
        var middleware = new ApiContractMiddleware(context =>
        {
            nextCalled = true;
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext
        {
            Request =
            {
                Method = HttpMethods.Post,
                Path = string.Format(pathTemplate, approvalId)
            },
            Response = { Body = new MemoryStream() }
        };
        context.Request.Headers["Idempotency-Key"] = "decision-key";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", TenantId.ToString())], "test"));

        await middleware.InvokeAsync(context, db, TimeProvider.System);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Idempotency-Replayed"));
    }

    private static CreateApprovalDto Request(string requester) => new(
        "release.execute", "Release", "Deploy verified artifact", Severity.High, requester, 60, ["evidence:test"]);

    private static ApprovalService CreateService(RegentDbContext db, TimeProvider clock, string displayName, Guid? userId = null)
    {
        var actorId = userId ?? Guid.NewGuid();
        if (!db.Memberships.Any(x => x.TenantId == TenantId && x.UserId == actorId))
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, UserId = actorId, Role = "Owner"
            });
        db.SaveChanges();
        return new ApprovalService(db, new TestTenantContext(TenantId, displayName, actorId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock), new ApprovalAuthority(db));
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestTenantContext(Guid tenantId, string displayName, Guid? userId) : ITenantContext
    {
        public TenantId TenantId { get; } = new(tenantId);
        public UserId UserId { get; } = new(userId ?? Guid.NewGuid());
        public string DisplayName => displayName;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    }
}
