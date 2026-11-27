using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class ComplianceProfileApiTests
{
    [Fact]
    public async Task New_tenant_gets_stable_baseline_and_versioned_updates_remain_tenant_scoped()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        using var firstGet = new HttpRequestMessage(HttpMethod.Get, "/v1/compliance/profile");
        TestIdentityRequest.Apply(firstGet, tenantId, role: "Owner", userId: actorId);
        using var firstResponse = await client.SendAsync(firstGet);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(firstBody));
        var baseline = JsonSerializer.Deserialize<ComplianceProfile>(firstBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(baseline);
        Assert.Equal(tenantId.ToString(), baseline.TenantId);
        Assert.Equal("baseline", baseline.Preset);
        Assert.Equal(["OpenAI", "Anthropic"], baseline.AllowedProviders);
        Assert.Equal(1, baseline.Version);

        using var repeatedGet = new HttpRequestMessage(HttpMethod.Get, "/v1/compliance/profile");
        TestIdentityRequest.Apply(repeatedGet, tenantId, role: "Owner", userId: actorId);
        using var repeatedResponse = await client.SendAsync(repeatedGet);
        var repeatedBody = await repeatedResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);
        Assert.Equal(firstBody, repeatedBody);

        using var update = new HttpRequestMessage(HttpMethod.Put, "/v1/compliance/profile")
        {
            Content = JsonContent.Create(baseline! with
            {
                TenantId = otherTenantId.ToString(),
                Preset = "strict",
                AllowedProviders = ["OpenAI"],
                OnlineRetentionDays = 365,
                AuditProfile = "worm",
                BackupRequired = true
            })
        };
        update.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        update.Headers.Add("If-Match", "\"1\"");
        TestIdentityRequest.Apply(update, tenantId, role: "Owner", userId: actorId);
        using var updateResponse = await client.SendAsync(update);
        var updatedBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = JsonSerializer.Deserialize<ComplianceProfile>(updatedBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(updated);
        Assert.Equal(tenantId.ToString(), updated.TenantId);
        Assert.Equal("strict", updated.Preset);
        Assert.Equal(2, updated.Version);

        using var updatedGet = new HttpRequestMessage(HttpMethod.Get, "/v1/compliance/profile");
        TestIdentityRequest.Apply(updatedGet, tenantId, role: "Owner", userId: actorId);
        using var updatedGetResponse = await client.SendAsync(updatedGet);
        Assert.Equal(HttpStatusCode.OK, updatedGetResponse.StatusCode);
        var current = JsonSerializer.Deserialize<ComplianceProfile>(await updatedGetResponse.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(current);
        Assert.Equal(2, current.Version);
        Assert.Equal("worm", current.AuditProfile);

        using var staleUpdate = new HttpRequestMessage(HttpMethod.Put, "/v1/compliance/profile")
        {
            Content = JsonContent.Create(baseline! with { Preset = "stale" })
        };
        staleUpdate.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        staleUpdate.Headers.Add("If-Match", "\"1\"");
        TestIdentityRequest.Apply(staleUpdate, tenantId, role: "Owner", userId: actorId);
        using var staleResponse = await client.SendAsync(staleUpdate);
        var staleBody = await staleResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);
        using var staleError = JsonDocument.Parse(staleBody);
        Assert.Equal(VersionConflictException.Code, staleError.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var afterConflictGet = new HttpRequestMessage(HttpMethod.Get, "/v1/compliance/profile");
        TestIdentityRequest.Apply(afterConflictGet, tenantId, role: "Owner", userId: actorId);
        using var afterConflictResponse = await client.SendAsync(afterConflictGet);
        var afterConflict = JsonSerializer.Deserialize<ComplianceProfile>(await afterConflictResponse.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(afterConflict);
        Assert.Equal(2, afterConflict.Version);
        Assert.Equal("worm", afterConflict.AuditProfile);

        using var crossTenant = new HttpRequestMessage(HttpMethod.Get, "/v1/compliance/profile");
        TestIdentityRequest.Apply(crossTenant, otherTenantId, role: "Owner", userId: actorId);
        crossTenant.Headers.Remove(TestAuthenticationHeaders.ManageMembership);
        using var crossTenantResponse = await client.SendAsync(crossTenant);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantResponse.StatusCode);

        var profilesPath = Path.Combine(factory.Root, "data", "modules", "compliance.json");
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(profilesPath));
        Assert.True(persisted.RootElement.TryGetProperty(tenantId.ToString(), out _));
        Assert.False(persisted.RootElement.TryGetProperty(otherTenantId.ToString(), out _));
    }

    [Fact]
    public void Compliance_service_and_tenant_purge_use_the_same_store_instance()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<ComplianceProfile>>();
        var tenantStore = Assert.Single(scope.ServiceProvider.GetServices<ITenantJsonDocumentStore>()
            .OfType<JsonDocumentStore<ComplianceProfile>>());

        Assert.Same(store, tenantStore);
    }
}
