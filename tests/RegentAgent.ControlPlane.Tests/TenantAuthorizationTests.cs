using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class TenantAuthorizationTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("POST", "/v1/tenants/{tenantId:guid}/invitations")]
    [InlineData("POST", "/v1/tenants/{tenantId:guid}/ownership-delegations")]
    [InlineData("PUT", "/v1/tenants/{tenantId:guid}/identity-providers/{provider}")]
    [InlineData("PUT", "/v1/charter")]
    [InlineData("DELETE", "/v1/models/catalog/{provider}/{modelId}")]
    [InlineData("POST", "/v1/runtime-policy/budgets")]
    [InlineData("PUT", "/v1/runtime-policy/schedule")]
    [InlineData("POST", "/v1/agent-evolution/tool-policies")]
    [InlineData("POST", "/v1/governance/incidents")]
    [InlineData("POST", "/v1/lifecycle/deletions")]
    [InlineData("POST", "/v1/workflows/{id}/review")]
    [InlineData("POST", "/v1/task-governance/task-types")]
    [InlineData("POST", "/v1/billing/checkout")]
    [InlineData("POST", "/v1/billing/invoices/{invoiceId:guid}/pay-local")]
    [InlineData("POST", "/v1/exports")]
    [InlineData("POST", "/v1/audit/checkpoints")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/findings")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/briefs/{briefId:guid}/decision")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/plans/{planId:guid}/approve")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/tasks/{taskId:guid}/complete")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/acceptance-reports")]
    [InlineData("POST", "/v1/projects/{projectId:guid}/delivery/releases")]
    public void Privileged_writes_require_shared_tenant_writer_policy(string method, string path)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var endpoint = FindEndpoint(factory, method, path);
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            data => data.Policy == ControlPlanePolicies.TenantWriter);
    }

    [Theory]
    [InlineData("POST", "/v1/employees/{employeeId:guid}/offboard")]
    [InlineData("POST", "/v1/employees/{employeeId:guid}/rehire")]
    [InlineData("POST", "/v1/candidates/{candidateId:guid}/hire")]
    [InlineData("POST", "/v1/performance-actions")]
    [InlineData("POST", "/v1/approvals/{id:guid}/decision")]
    public void Approval_routes_require_approval_decider_policy(string method, string path)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var endpoint = FindEndpoint(factory, method, path);
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            data => data.Policy == ControlPlanePolicies.ApprovalDecider);
    }

    [Theory]
    [InlineData("GET", "/v1/collaboration/graph")]
    [InlineData("GET", "/v1/collaboration/trace")]
    [InlineData("GET", "/v1/collaboration/requests")]
    [InlineData("GET", "/v1/collaboration/requests/{id:guid}")]
    [InlineData("GET", "/v1/conversations")]
    [InlineData("GET", "/v1/conversations/{id}/messages")]
    public void Human_collaboration_and_chat_reads_use_named_policies(string method, string path)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var endpoint = FindEndpoint(factory, method, path);
        var expected = path.StartsWith("/v1/conversations", StringComparison.Ordinal)
            ? ControlPlanePolicies.ChatRead : ControlPlanePolicies.CollaborationRead;
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == expected);
    }

    [Fact]
    public async Task Authorization_precedes_idempotency_validation_and_password_is_not_a_bypass()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var denied = CreateBudgetWrite();
        TestIdentityRequest.Apply(denied, TenantId, capabilities: []);
        using var deniedResponse = await client.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var authorized = CreateBudgetWrite();
        TestIdentityRequest.Apply(authorized, TenantId, capabilities: [ControlPlanePolicies.TenantWriteCapability]);
        using var authorizedResponse = await client.SendAsync(authorized);
        Assert.Equal(HttpStatusCode.BadRequest, authorizedResponse.StatusCode);

        using var local = CreateBudgetWrite();
        TestIdentityRequest.Apply(local, TenantId, authMethod: "Password");
        using var localResponse = await client.SendAsync(local);
        Assert.Equal(HttpStatusCode.Forbidden, localResponse.StatusCode);
    }

    [Fact]
    public async Task Current_membership_is_required_and_chat_read_resolves_employee_or_owner_observer()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId, Role = "Member",
            CapabilitiesJson = "[\"chat.read\",\"tenant.write\"]"
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

        var external = Principal("Member", "Oidc", [], employeeId.ToString(), TenantId, userId);
        Assert.True((await authorization.AuthorizeAsync(external, null, ControlPlanePolicies.TenantWriter)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(external, null, ControlPlanePolicies.ChatRead)).Succeeded);

        var missingTenant = Principal("Owner", "Oidc", [], "employee-1", null, userId);
        Assert.False((await authorization.AuthorizeAsync(missingTenant, null, ControlPlanePolicies.TenantWriter)).Succeeded);
        var invalidUser = Principal("Owner", "Oidc", [], "employee-1", TenantId, null);
        Assert.False((await authorization.AuthorizeAsync(invalidUser, null, ControlPlanePolicies.TenantWriter)).Succeeded);
        var missingMembership = Principal("Owner", "Oidc", [], employeeId.ToString(), TenantId, Guid.NewGuid());
        Assert.False((await authorization.AuthorizeAsync(missingMembership, null, ControlPlanePolicies.TenantWriter)).Succeeded);
        var ownerUserId = Guid.NewGuid();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = ownerUserId, Role = "Owner", CapabilitiesJson = "[]"
        });
        await db.SaveChangesAsync();
        var ownerWithoutEmployee = Principal("Member", "Oidc", [], null, TenantId, ownerUserId);
        Assert.True((await authorization.AuthorizeAsync(ownerWithoutEmployee, null, ControlPlanePolicies.ChatRead)).Succeeded);
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("role")]
    [InlineData("capability")]
    public async Task Revoking_current_membership_authority_denies_the_next_protected_request(string revocation)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var actorId = Guid.NewGuid();
        var role = revocation == "capability" ? "Member" : "Owner";
        string[] capabilities = revocation == "capability" ? ["dashboard.read"] : [];
        using (var first = new HttpRequestMessage(HttpMethod.Get, "/v1/dashboard/summary"))
        {
            TestIdentityRequest.Apply(first, TenantId, role: role, capabilities: capabilities, userId: actorId);
            using var response = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == actorId);
            if (revocation == "membership") db.Memberships.Remove(membership);
            else
            {
                membership.Role = "Member";
                membership.CapabilitiesJson = "[]";
            }
            await db.SaveChangesAsync();
        }

        using var stale = new HttpRequestMessage(HttpMethod.Get, "/v1/dashboard/summary");
        TestIdentityRequest.Apply(stale, TenantId, role: role, capabilities: capabilities, userId: actorId);
        using var denied = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Revoked_membership_denies_ordinary_tenant_reads()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var actorId = Guid.NewGuid();
        using (var first = new HttpRequestMessage(HttpMethod.Get, "/v1/models/pool"))
        {
            TestIdentityRequest.Apply(first, TenantId, userId: actorId);
            using var response = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == actorId);
            db.Memberships.Remove(membership);
            await db.SaveChangesAsync();
        }

        using var stale = new HttpRequestMessage(HttpMethod.Get, "/v1/models/pool");
        TestIdentityRequest.Apply(stale, TenantId, userId: actorId);
        using var denied = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Memory_governor_writes_use_the_current_membership_role()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var actorId = Guid.NewGuid();
        using (var first = new HttpRequestMessage(HttpMethod.Post, "/v1/memory/index/rebuild"))
        {
            first.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            TestIdentityRequest.Apply(first, TenantId, role: "Owner", userId: actorId);
            using var response = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == actorId);
            membership.Role = "Member";
            await db.SaveChangesAsync();
        }

        using var stale = new HttpRequestMessage(HttpMethod.Post, "/v1/memory/index/rebuild");
        stale.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        TestIdentityRequest.Apply(stale, TenantId, role: "Owner", userId: actorId);
        using var denied = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Delivery_mutations_deny_authenticated_members_without_capabilities()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1/projects/{Guid.NewGuid()}/delivery/findings")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        TestIdentityRequest.Apply(request, TenantId, capabilities: []);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Harmless_authenticated_read_does_not_require_tenant_writer()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models/pool");
        TestIdentityRequest.Apply(request, TenantId, capabilities: []);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Missing_tenant_claim_does_not_fall_back_to_demo_tenant()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/models/pool");
        request.Headers.Add(TestAuthenticationHeaders.Authenticated, "true");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpRequestMessage CreateBudgetWrite()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/runtime-policy/budgets")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        return request;
    }

    private static ClaimsPrincipal Principal(string role, string? authMethod, string[] capabilities, string? employeeId,
        Guid? tenantId = null, Guid? userId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (tenantId is { } tenant) claims.Add(new Claim("tenant_id", tenant.ToString()));
        if (userId is { } user) claims.Add(new Claim(ClaimTypes.NameIdentifier, user.ToString()));
        if (authMethod is not null) claims.Add(new Claim("auth_method", authMethod));
        if (employeeId is not null) claims.Add(new Claim("employee_id", employeeId));
        claims.AddRange(capabilities.Select(value => new Claim("capability", value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static RouteEndpoint FindEndpoint(ControlPlaneSecurityHttpFactory factory, string method, string path)
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        return Assert.Single(endpoints, endpoint => endpoint.RoutePattern.RawText == path
            && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);
    }
}
