using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;

namespace RegentAgent.Gateway;

public sealed class AgentAssignmentAccessValidator(IHttpClientFactory clients)
{
    public async Task ValidateAsync(string token, AgentIdentityModel identity, CancellationToken cancellationToken)
    {
        if (identity.CredentialEpoch is not { } epoch || epoch == Guid.Empty)
            throw new SecurityTokenException("Agent assignment credential epoch is required.");

        var client = clients.CreateClient("controlplane-assignment");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new SecurityTokenException("Agent assignment is not active in ControlPlane.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("tenantId", out var tenantValue)
                || !Guid.TryParse(tenantValue.GetString(), out var tenantId)
                || !root.TryGetProperty("employeeId", out var employeeValue)
                || !Guid.TryParse(employeeValue.GetString(), out var employeeId)
                || !root.TryGetProperty("credentialEpoch", out var epochValue)
                || !Guid.TryParse(epochValue.GetString(), out var currentEpoch)
                || tenantId != identity.TenantId || employeeId != identity.EmployeeId || currentEpoch != epoch)
                throw new SecurityTokenException("ControlPlane rejected the agent assignment identity.");
        }
        catch (HttpRequestException exception)
        {
            throw new SecurityTokenException("ControlPlane assignment validation is unavailable.", exception);
        }
        catch (JsonException exception)
        {
            throw new SecurityTokenException("ControlPlane assignment validation response is invalid.", exception);
        }
    }
}
