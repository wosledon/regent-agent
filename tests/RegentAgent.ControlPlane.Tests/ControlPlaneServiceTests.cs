using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ControlPlaneServiceTests
{
    [Fact]
    public async Task Dashboard_is_scoped_to_current_tenant()
    {
        await using var db = CreateDb();
        var clock = new FixedTimeProvider();
        await DbInitializer.InitializeAsync(db, CancellationToken.None, clock);
        var otherTenant = Guid.NewGuid();
        db.Projects.Add(new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = otherTenant, Name = "Other tenant project",
            Owner = "Other", Stage = ProjectStage.Building, Health = ProjectHealth.OnTrack,
            TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new DashboardService(db, new TestTenantContext(), new FixedTimeProvider());
        var summary = await service.GetAsync(CancellationToken.None);

        var projectCard = Assert.Single(summary.Cards, card => card.Id == "projects");
        Assert.Contains("3 个项目", projectCard.Summary);
        Assert.DoesNotContain("Other tenant project", projectCard.Summary);
    }

    [Fact]
    public async Task Approval_decision_is_audited_and_cannot_be_repeated()
    {
        await using var db = CreateDb();
        var clock = new FixedTimeProvider();
        await DbInitializer.InitializeAsync(db, CancellationToken.None, clock);
        var approval = await db.Approvals.FirstAsync();
        var audit = new AuditIntegrityService(db, clock);
        await audit.BackfillAsync(CancellationToken.None);
        var service = new ApprovalService(db, new TestTenantContext(Guid.Parse("33333333-3333-3333-3333-333333333333")), clock, audit,
            new IdempotencyService(db, clock), new ApprovalAuthority(db));

        var result = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("approve", "Verified"), CancellationToken.None, "approval-key-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.Approved, result.Value!.State);
        Assert.Contains(await db.AuditRecords.ToListAsync(), item => item.EventType == "approval.decided" && item.Seq == 2);
        var repeated = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("approve", "Verified"), CancellationToken.None, "approval-key-1");
        Assert.True(repeated.IsSuccess);
        Assert.Equal(ApprovalState.Approved, repeated.Value!.State);
        var conflict = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("reject", "Changed mind"), CancellationToken.None, "approval-key-1");
        Assert.False(conflict.IsSuccess);
        Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.Error!.Code);
        Assert.Single(await db.AuditRecords.Where(x => x.EventType == "approval.decided").ToListAsync());
    }

    [Fact]
    public async Task Seeded_approvals_are_fresh_and_independently_decidable()
    {
        await using var db = CreateDb();
        var clock = new FixedTimeProvider();
        await DbInitializer.InitializeAsync(db, CancellationToken.None, clock);
        var approvals = await db.Approvals.ToListAsync();
        var approverId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        Assert.NotEmpty(approvals);
        Assert.All(approvals, approval =>
        {
            Assert.Equal(ApprovalState.Pending, approval.State);
            Assert.True(approval.ExpiresAt > clock.GetUtcNow());
            Assert.NotEqual(approverId, approval.RequesterUserId);
        });
        var service = new ApprovalService(db, new TestTenantContext(approverId), clock,
            new AuditIntegrityService(db, clock), new IdempotencyService(db, clock), new ApprovalAuthority(db));
        var result = await service.DecideAsync(new ApprovalId(approvals[0].Id),
            new ApprovalDecision("approve", "Fresh seed verified"), CancellationToken.None, "seed-decision");

        Assert.True(result.IsSuccess);
        Assert.Equal(approverId, result.Value!.DecidedBy);
    }

    [Fact]
    public async Task Expired_approvals_default_to_rejected_and_are_audited()
    {
        await using var db = CreateDb();
        var clock = new FixedTimeProvider();
        await DbInitializer.InitializeAsync(db, CancellationToken.None, clock);
        var approval = new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ActionType = "release.execute", Title = "Expired release", Impact = "No decision", Severity = Severity.High,
            Requester = "Controller", CreatedAt = clock.GetUtcNow().AddHours(-2), ExpiresAt = clock.GetUtcNow().AddMinutes(-1)
        };
        db.Approvals.Add(approval);
        await db.SaveChangesAsync();
        var audit = new AuditIntegrityService(db, clock);
        await audit.BackfillAsync(CancellationToken.None);
        var service = new ApprovalExpiryService(db, audit, clock);

        var expired = await service.ExpireDueAsync(CancellationToken.None);

        Assert.Equal(1, expired);
        Assert.Equal(ApprovalState.Expired, (await db.Approvals.FindAsync(approval.Id))!.State);
        Assert.Contains(await db.AuditRecords.ToListAsync(), item => item.EventType == "approval.expired" && item.PayloadJson.Contains(approval.Id.ToString()));
        Assert.Contains(await db.Notifications.ToListAsync(), item => item.Title.EndsWith("Expired"));
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestTenantContext(Guid? userId = null) : ITenantContext
    {
        public TenantId TenantId { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        public UserId UserId { get; } = new(userId ?? Guid.Parse("22222222-2222-2222-2222-222222222222"));
        public string DisplayName => "Demo Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
    }
}
