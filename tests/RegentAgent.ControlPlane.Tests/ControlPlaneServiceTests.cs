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
        await DbInitializer.InitializeAsync(db, CancellationToken.None);
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
        await DbInitializer.InitializeAsync(db, CancellationToken.None);
        var approval = await db.Approvals.FirstAsync();
        var audit = new AuditIntegrityService(db, new FixedTimeProvider());
        await audit.BackfillAsync(CancellationToken.None);
        var service = new ApprovalService(db, new TestTenantContext(), new FixedTimeProvider(), audit);

        var result = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("approve", "Verified"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalState.Approved, result.Value!.State);
        Assert.Contains(await db.AuditRecords.ToListAsync(), item => item.EventType == "approval.decided" && item.Seq == 2);
        var repeated = await service.DecideAsync(new ApprovalId(approval.Id), new ApprovalDecision("reject", "Changed mind"), CancellationToken.None);
        Assert.False(repeated.IsSuccess);
        Assert.Equal("INVALID_STATE", repeated.Error!.Code);
    }

    private static RegentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<RegentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class TestTenantContext : ITenantContext
    {
        public TenantId TenantId { get; } = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        public UserId UserId { get; } = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        public string DisplayName => "Demo Owner";
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset now = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
    }
}
