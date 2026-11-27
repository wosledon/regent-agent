using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RegentAgent.Contracts.Memory;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class MemoryAuthorizationApiTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Human_memory_endpoints_use_authenticated_user_id_not_display_name()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var aliceId = Guid.NewGuid();
        var bobId = Guid.NewGuid();
        string claimId;
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/v1/memories")
        {
            Content = JsonContent.Create(new MemoryCaptureRequest("personal", "Private launch note",
                "source:private", 0.8, "trusted"))
        })
        {
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            TestIdentityRequest.Apply(request, TenantId, userId: aliceId, name: "Same Name");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            claimId = payload.RootElement.GetProperty("id").GetString()!;
            Assert.Equal(aliceId.ToString(), payload.RootElement.GetProperty("authorUserId").GetString());
        }

        using (var denied = new HttpRequestMessage(HttpMethod.Get, $"/v1/memories/{claimId}"))
        {
            TestIdentityRequest.Apply(denied, TenantId, userId: bobId, name: "Same Name");
            using var response = await client.SendAsync(denied);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using (var allowed = new HttpRequestMessage(HttpMethod.Get, $"/v1/memories/{claimId}"))
        {
            TestIdentityRequest.Apply(allowed, TenantId, userId: aliceId, name: "Same Name");
            using var response = await client.SendAsync(allowed);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var search = new HttpRequestMessage(HttpMethod.Get,
            "/v1/memories/search?query=Private%20launch%20note&scope=personal");
        TestIdentityRequest.Apply(search, TenantId, userId: bobId, name: "Same Name");
        using var searchResponse = await client.SendAsync(search);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        using var searchPayload = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        Assert.Empty(searchPayload.RootElement.GetProperty("hits").EnumerateArray());
    }
}
