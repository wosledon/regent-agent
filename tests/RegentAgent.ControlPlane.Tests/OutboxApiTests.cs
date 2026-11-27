using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class OutboxApiTests
{
    [Theory]
    [InlineData("/v1/outbox", HttpStatusCode.MethodNotAllowed)]
    [InlineData("/v1/outbox/process", HttpStatusCode.NotFound)]
    public async Task Tenants_cannot_publish_or_process_internal_outbox_events(string path, HttpStatusCode expected)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { eventType = "approval.execution.committed", payload = new { } })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        TestIdentityRequest.Apply(request, Guid.Parse("11111111-1111-1111-1111-111111111111"),
            role: "Owner", authMethod: "Password");

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }
}
