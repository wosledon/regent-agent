using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class ApprovalAuthorizationApiTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Member_password_is_denied_approval_decision_and_execution_routes()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        Guid approvalId;
        using (var dbScope = factory.Services.CreateScope())
            approvalId = (await dbScope.ServiceProvider.GetRequiredService<RegentDbContext>().Approvals.FirstAsync()).Id;
        var actorId = Guid.NewGuid();

        using var decision = new HttpRequestMessage(HttpMethod.Post, $"/v1/approvals/{approvalId}/decision")
        {
            Content = JsonContent.Create(new ApprovalDecision("approve", "Member attempt"))
        };
        decision.Headers.Add("Idempotency-Key", "member-decision");
        TestIdentityRequest.Apply(decision, TenantId, role: "Member", authMethod: "Password",
            capabilities: ["tenant.write"], userId: actorId);
        using var decisionResponse = await client.SendAsync(decision);
        Assert.Equal(HttpStatusCode.Forbidden, decisionResponse.StatusCode);

        using var execution = new HttpRequestMessage(HttpMethod.Post, $"/v1/employees/{Guid.NewGuid()}/offboard")
        {
            Content = JsonContent.Create(new OffboardRequest(Guid.NewGuid().ToString()))
        };
        execution.Headers.Add("Idempotency-Key", "member-execution");
        TestIdentityRequest.Apply(execution, TenantId, role: "Member", authMethod: "Password",
            capabilities: ["tenant.write"], userId: actorId);
        using var executionResponse = await client.SendAsync(execution);
        Assert.Equal(HttpStatusCode.Forbidden, executionResponse.StatusCode);
    }

    [Fact]
    public async Task T2_execution_requires_the_exact_current_membership_capability()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        using (var dbScope = factory.Services.CreateScope())
        {
            var db = dbScope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, UserId = actorId, Role = "Member",
                CapabilitiesJson = "[\"tenant.write\"]"
            });
            db.Employees.Add(new EmployeeRecord
            {
                Id = employeeId, TenantId = TenantId, EmployeeNo = "EMP-AUTH", DisplayName = "Employee",
                Position = "Engineering", State = EmployeeState.Active
            });
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId,
                PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
            });
            db.Approvals.Add(new ApprovalRecord
            {
                Id = approvalId, TenantId = TenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
                Title = "Offboard", Impact = "Ends employment", Severity = Severity.High,
                State = ApprovalState.Approved, Requester = "Requester", RequesterUserId = Guid.NewGuid(),
                TargetEmployeeId = employeeId,
                ActionHash = new ApprovalBinding(TenantId, ApprovalActionTypes.EmployeeOffboard,
                    TargetEmployeeId: employeeId).ComputeV2Hash(),
                DecidedBy = Guid.NewGuid(), DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        using (var denied = CreateOffboardRequest(employeeId, approvalId, "denied"))
        {
            TestIdentityRequest.Apply(denied, TenantId, role: "Member", authMethod: "Password",
                capabilities: ["tenant.write"], userId: actorId);
            using var response = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var dbScope = factory.Services.CreateScope())
        {
            var db = dbScope.ServiceProvider.GetRequiredService<RegentDbContext>();
            (await db.Memberships.SingleAsync(x => x.UserId == actorId)).CapabilitiesJson =
                "[\"approval.employee.offboard\"]";
            await db.SaveChangesAsync();
        }
        using var allowed = CreateOffboardRequest(employeeId, approvalId, "allowed");
        TestIdentityRequest.Apply(allowed, TenantId, role: "Member", authMethod: "Password",
            capabilities: ["approval.employee.offboard"], userId: actorId);
        using var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task T2_decision_checks_action_capability_and_persists_decider()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = TenantId, UserId = actorId, Role = "Member",
                CapabilitiesJson = "[\"approval.hiring.hire\"]"
            });
            db.Employees.Add(new EmployeeRecord
            {
                Id = employeeId, TenantId = TenantId, EmployeeNo = "EMP-DECIDER", DisplayName = "Employee",
                Position = "Engineering", State = EmployeeState.Active
            });
            db.Approvals.Add(new ApprovalRecord
            {
                Id = approvalId, TenantId = TenantId, ActionType = ApprovalActionTypes.EmployeeOffboard,
                Title = "Offboard", Impact = "Ends employment", Severity = Severity.High,
                Requester = "Requester", RequesterUserId = Guid.NewGuid(), TargetEmployeeId = employeeId,
                ActionHash = new ApprovalBinding(TenantId, ApprovalActionTypes.EmployeeOffboard,
                    TargetEmployeeId: employeeId).ComputeV2Hash(),
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }

        using (var denied = CreateDecisionRequest(approvalId, "denied"))
        {
            TestIdentityRequest.Apply(denied, TenantId, role: "Member", authMethod: "Password",
                capabilities: ["approval.hiring.hire"], userId: actorId);
            using var response = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var updateScope = factory.Services.CreateScope())
        {
            var db = updateScope.ServiceProvider.GetRequiredService<RegentDbContext>();
            (await db.Memberships.SingleAsync(x => x.UserId == actorId)).CapabilitiesJson =
                "[\"approval.employee.offboard\"]";
            await db.SaveChangesAsync();
        }
        using var allowed = CreateDecisionRequest(approvalId, "allowed");
        TestIdentityRequest.Apply(allowed, TenantId, role: "Member", authMethod: "Password",
            capabilities: ["approval.employee.offboard"], userId: actorId);
        using var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var decided = await verifyScope.ServiceProvider.GetRequiredService<RegentDbContext>().Approvals.SingleAsync(x => x.Id == approvalId);
        Assert.Equal(actorId, decided.DecidedBy);
        Assert.NotNull(decided.DecidedAt);
    }

    private static HttpRequestMessage CreateOffboardRequest(Guid employeeId, Guid approvalId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/employees/{employeeId}/offboard")
        {
            Content = JsonContent.Create(new OffboardRequest(approvalId.ToString()))
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static HttpRequestMessage CreateDecisionRequest(Guid approvalId, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/approvals/{approvalId}/decision")
        {
            Content = JsonContent.Create(new ApprovalDecision("approve", "Evidence verified"))
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
