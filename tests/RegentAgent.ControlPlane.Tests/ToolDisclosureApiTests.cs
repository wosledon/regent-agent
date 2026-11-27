using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.Agents;
using RegentAgent.Contracts.Agents;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class ToolDisclosureApiTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Theory]
    [InlineData("Member", new string[0], new string[] { "public" })]
    [InlineData("Member", new string[] { "org.update" }, new string[] { "public" })]
    [InlineData("Member", new string[] { "tools.restricted.activate" }, new string[] { "public" })]
    [InlineData("Member", new string[] { "org.update", "tools.restricted.activate" }, new string[] { "public", "restricted" })]
    [InlineData("Owner", new string[] { "org.update" }, new string[] { "public", "restricted" })]
    public async Task Disclosure_uses_server_claims_and_role_not_forged_request_authority(
        string role, string[] claims, string[] expectedTools)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var evolution = scope.ServiceProvider.GetRequiredService<AgentEvolutionService>();
        await SeedPoliciesAsync(evolution);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/agent-evolution/tools/disclose")
        {
            Content = JsonContent.Create(new ToolDisclosureRequest(["org.update", "forged.capability"], true))
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        TestIdentityRequest.Apply(request, TenantId, role: role, capabilities: claims);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tools = payload.RootElement.GetProperty("tools").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()).ToArray();
        Assert.Equal(expectedTools.OrderBy(x => x), tools.OrderBy(x => x));
        Assert.DoesNotContain("forbidden", tools);
        Assert.False(payload.RootElement.GetProperty("permissionExpanded").GetBoolean());
    }

    [Fact]
    public async Task Revoked_capability_cannot_be_replaced_by_stale_cookie_claims()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await SeedPoliciesAsync(scope.ServiceProvider.GetRequiredService<AgentEvolutionService>());
        var actorId = Guid.NewGuid();
        using (var first = CreateDisclosureRequest(Guid.NewGuid().ToString()))
        {
            TestIdentityRequest.Apply(first, TenantId, role: "Owner", capabilities: ["org.update"], userId: actorId);
            using var response = await client.SendAsync(first);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(["public", "restricted"], await ReadToolNamesAsync(response));
        }
        using (var mutation = factory.Services.CreateScope())
        {
            var db = mutation.ServiceProvider.GetRequiredService<RegentDbContext>();
            var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == actorId);
            membership.CapabilitiesJson = "[]";
            await db.SaveChangesAsync();
        }

        using var stale = CreateDisclosureRequest(Guid.NewGuid().ToString());
        TestIdentityRequest.Apply(stale, TenantId, role: "Owner", capabilities: ["org.update"], userId: actorId);
        using var denied = await client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Equal(["public"], await ReadToolNamesAsync(denied));
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Development")]
    public async Task Member_authentication_method_does_not_activate_restricted_tools(string authMethod)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await SeedPoliciesAsync(scope.ServiceProvider.GetRequiredService<AgentEvolutionService>());
        using var request = CreateDisclosureRequest(Guid.NewGuid().ToString());
        TestIdentityRequest.Apply(request, TenantId, role: "Member", authMethod: authMethod,
            capabilities: ["org.update"]);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["public"], await ReadToolNamesAsync(response));
    }

    [Theory]
    [InlineData("/v1/agent-evolution/tools/disclose")]
    [InlineData("/v1/AGENT-EVOLUTION/TOOLS/DISCLOSE/")]
    public async Task Idempotency_replay_cannot_reuse_another_actors_restricted_disclosure(string path)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        await SeedPoliciesAsync(scope.ServiceProvider.GetRequiredService<AgentEvolutionService>());
        const string key = "shared-disclosure-key";
        using var privileged = CreateDisclosureRequest(key, path);
        TestIdentityRequest.Apply(privileged, TenantId,
            capabilities: ["org.update", ControlPlanePolicies.RestrictedToolActivationCapability]);
        using var privilegedResponse = await client.SendAsync(privileged);
        Assert.Equal(HttpStatusCode.OK, privilegedResponse.StatusCode);
        Assert.Contains("restricted", await ReadToolNamesAsync(privilegedResponse));

        using var unprivileged = CreateDisclosureRequest(key, path);
        TestIdentityRequest.Apply(unprivileged, TenantId, capabilities: ["org.update"]);
        using var unprivilegedResponse = await client.SendAsync(unprivileged);
        Assert.Equal(HttpStatusCode.OK, unprivilegedResponse.StatusCode);
        Assert.Equal(["public"], await ReadToolNamesAsync(unprivilegedResponse));
    }

    private static HttpRequestMessage CreateDisclosureRequest(string key, string path = "/v1/agent-evolution/tools/disclose")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new ToolDisclosureRequest(["org.update"], true))
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static async Task<string[]> ReadToolNamesAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("tools").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!).OrderBy(name => name).ToArray();
    }

    private static async Task SeedPoliciesAsync(AgentEvolutionService evolution)
    {
        await evolution.SetToolPolicyAsync(TenantId, new ToolDisclosurePolicy
        {
            ToolName = "public", Level = "Primary", Summary = "Public summary"
        }, CancellationToken.None);
        await evolution.SetToolPolicyAsync(TenantId, new ToolDisclosurePolicy
        {
            ToolName = "restricted", Level = "Restricted", RequiredCapabilities = ["org.update"],
            Summary = "Restricted summary"
        }, CancellationToken.None);
        await evolution.SetToolPolicyAsync(TenantId, new ToolDisclosurePolicy
        {
            ToolName = "forbidden", Level = "Forbidden", Summary = "Forbidden summary"
        }, CancellationToken.None);
    }
}
