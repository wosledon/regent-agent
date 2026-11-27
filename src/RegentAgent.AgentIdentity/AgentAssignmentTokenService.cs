using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace RegentAgent.AgentIdentity;

public sealed class AgentAssignmentTokenService(IAgentIdentityKeyProvider keys, AgentIdentityOptions options)
{
    private readonly IAgentIdentityKeyProvider keys = keys;
    private readonly AgentIdentityOptions options = options;

    public AgentAssignmentToken Issue(AgentAssignmentRequest request)
    {
        options.Validate();
        if (request.Scopes.Any(scope => scope.Contains(' ') || scope.StartsWith("admin", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Employee assignment scopes are invalid.");
        if (request.CredentialEpoch is not { } credentialEpoch || credentialEpoch == Guid.Empty)
            throw new InvalidOperationException("Employee assignment credential epoch is required.");
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(options.LifetimeMinutes);
        var tokenId = Guid.NewGuid().ToString("N");
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", request.EmployeeId.ToString()),
            new("aud", options.Audience),
            new("aud", options.GatewayAudience),
            new("tenant_id", request.TenantId.ToString()),
            new("employee_id", request.EmployeeId.ToString()),
            new("credential_epoch", credentialEpoch.ToString()),
            new("scopes", string.Join(' ', request.Scopes)),
            new("jti", tokenId),
            new("iat", now.ToUnixTimeSeconds().ToString(), System.Security.Claims.ClaimValueTypes.Integer64)
        };
        if (request.TaskId is { } taskId) claims.Add(new("task_id", taskId.ToString()));
        if (!string.IsNullOrWhiteSpace(request.RunnerId)) claims.Add(new("runner_id", request.RunnerId));
        var key = new SymmetricSecurityKey(keys.GetSigningKey());
        var jwt = new JwtSecurityToken(options.Issuer, null, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var identity = new AgentIdentity(request.TenantId, request.EmployeeId, request.TaskId, request.RunnerId,
            request.Scopes.ToArray(), tokenId, now, expires, credentialEpoch);
        return new AgentAssignmentToken(new JwtSecurityTokenHandler().WriteToken(jwt), identity);
    }
}
