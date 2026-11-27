using System.Net;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;

namespace RegentAgent.Gateway.Tests;

public sealed class AgentAssignmentAccessValidatorTests
{
    [Fact]
    public async Task Active_controlplane_identity_is_required()
    {
        var identity = Identity();
        var validator = new AgentAssignmentAccessValidator(new FixedHttpClientFactory(new HttpClient(
            new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json(Json(identity)))))));

        await validator.ValidateAsync("token", identity, CancellationToken.None);
    }

    [Fact]
    public async Task Mismatched_epoch_is_rejected()
    {
        var identity = Identity();
        var payload = Json(identity with { CredentialEpoch = Guid.NewGuid() });
        var validator = new AgentAssignmentAccessValidator(new FixedHttpClientFactory(new HttpClient(
            new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json(payload))))));

        await Assert.ThrowsAsync<SecurityTokenException>(() => validator.ValidateAsync("token", identity, CancellationToken.None));
    }

    [Fact]
    public async Task Controlplane_rejection_fails_closed()
    {
        var validator = new AgentAssignmentAccessValidator(new FixedHttpClientFactory(new HttpClient(
            new FakeHttpMessageHandler((_, _) => Task.FromResult(FakeHttpMessageHandler.Json("{}", HttpStatusCode.Forbidden))))));

        await Assert.ThrowsAsync<SecurityTokenException>(() => validator.ValidateAsync("token", Identity(), CancellationToken.None));
    }

    [Fact]
    public async Task Revocation_is_observed_on_the_next_request()
    {
        var identity = Identity();
        var calls = 0;
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? FakeHttpMessageHandler.Json(Json(identity))
                : FakeHttpMessageHandler.Json("{}", HttpStatusCode.Forbidden));
        });
        var validator = new AgentAssignmentAccessValidator(new FixedHttpClientFactory(new HttpClient(handler)));

        await validator.ValidateAsync("token", identity, CancellationToken.None);
        await Assert.ThrowsAsync<SecurityTokenException>(() =>
            validator.ValidateAsync("token", identity, CancellationToken.None));

        Assert.Equal(2, calls);
    }

    private static AgentIdentityModel Identity() => new(Guid.NewGuid(), Guid.NewGuid(), null, null,
        ["infer:employee"], Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMinutes(15), Guid.NewGuid());

    private static string Json(AgentIdentityModel identity) => $$"""
        {"tenantId":"{{identity.TenantId}}","employeeId":"{{identity.EmployeeId}}","credentialEpoch":"{{identity.CredentialEpoch}}"}
        """;

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            client.BaseAddress ??= new Uri("http://controlplane.test");
            return client;
        }
    }
}
