using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class MembershipAuthorityTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("Owner")]
    [InlineData("CoOwner")]
    public async Task Current_database_owner_without_employee_is_an_observer_despite_stale_claims(string role)
    {
        await using var db = CreateDb();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = role, CapabilitiesJson = "[]"
        });
        await db.SaveChangesAsync();
        var staleClaims = Principal("Member", ["chat.read", "collaboration.read"]);

        var viewer = await new MembershipAuthority(db).ResolveChatViewerAsync(staleClaims);

        Assert.NotNull(viewer);
        Assert.IsType<OwnerObserverViewer>(viewer.Viewer);
        Assert.Equal(role, viewer.Membership.Role);
        Assert.Empty(viewer.Membership.Capabilities);
    }

    [Fact]
    public async Task Valid_employee_identity_is_member_scoped_even_for_an_owner()
    {
        await using var db = CreateDb();
        var employeeId = Guid.NewGuid();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner", CapabilitiesJson = "[]"
        });
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = TenantId, EmployeeNo = $"EMP-{employeeId:N}",
            DisplayName = "Employee", Position = "Engineering"
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, PositionId = Guid.NewGuid(),
            Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var viewer = await new MembershipAuthority(db).ResolveChatViewerAsync(
            Principal("Owner", [], employeeId.ToString()));

        Assert.NotNull(viewer);
        var employee = Assert.IsType<EmployeeMemberViewer>(viewer.Viewer);
        Assert.Equal(employeeId.ToString(), employee.EmployeeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Non_owner_without_valid_employee_fails_closed(string? employeeId)
    {
        await using var db = CreateDb();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Member",
            CapabilitiesJson = "[\"chat.read\",\"collaboration.read\"]"
        });
        await db.SaveChangesAsync();

        var viewer = await new MembershipAuthority(db).ResolveChatViewerAsync(
            Principal("Owner", ["chat.read"], employeeId));

        Assert.Null(viewer);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[\"\"]")]
    [InlineData("not-json")]
    public async Task Malformed_database_capabilities_fail_closed(string capabilitiesJson)
    {
        await using var db = CreateDb();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner",
            CapabilitiesJson = capabilitiesJson
        });
        await db.SaveChangesAsync();

        Assert.Null(await new MembershipAuthority(db).ResolveChatViewerAsync(Principal("Owner", [])));
    }

    [Fact]
    public async Task Downgrade_and_removed_membership_are_observed_immediately()
    {
        await using var db = CreateDb();
        var membership = new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Owner", CapabilitiesJson = "[]"
        };
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();
        var authority = new MembershipAuthority(db);
        var staleOwner = Principal("Owner", ["chat.read"]);
        Assert.NotNull(await authority.ResolveChatViewerAsync(staleOwner));

        membership.Role = "Member";
        membership.CapabilitiesJson = "[]";
        await db.SaveChangesAsync();
        Assert.Null(await authority.ResolveChatViewerAsync(staleOwner));

        membership.Role = "Owner";
        await db.SaveChangesAsync();
        Assert.NotNull(await authority.ResolveChatViewerAsync(staleOwner));
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync();
        Assert.Null(await authority.ResolveChatViewerAsync(staleOwner));
    }

    private static ClaimsPrincipal Principal(string role, string[] capabilities, string? employeeId = null)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", TenantId.ToString()),
            new(ClaimTypes.NameIdentifier, UserId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (employeeId is not null) claims.Add(new Claim("employee_id", employeeId));
        claims.AddRange(capabilities.Select(value => new Claim("capability", value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
