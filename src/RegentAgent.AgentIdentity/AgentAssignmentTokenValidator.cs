using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace RegentAgent.AgentIdentity;

public sealed class AgentAssignmentTokenValidator(IAgentIdentityKeyProvider keys, AgentIdentityOptions options)
{
    private readonly IAgentIdentityKeyProvider keys = keys;
    private readonly AgentIdentityOptions options = options;

    public AgentIdentity Validate(string token, string? expectedAudience = null)
    {
        options.Validate();
        var validation = new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudiences = [expectedAudience ?? options.Audience],
            IssuerSigningKey = new SymmetricSecurityKey(keys.GetSigningKey()),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds)
        };
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validation, out var validated);
        if (validated is not JwtSecurityToken jwt || jwt.Header.Alg != SecurityAlgorithms.HmacSha256)
            throw new SecurityTokenException("Agent token algorithm is not allowed.");
        if (!principal.TryGetGuid("tenant_id", out var tenantId) || !principal.TryGetGuid("employee_id", out var employeeId)
            || !principal.TryGetGuid("credential_epoch", out var credentialEpoch) || credentialEpoch == Guid.Empty)
            throw new SecurityTokenException("Agent token tenant, employee and credential epoch are required.");
        var taskId = principal.TryGetGuid("task_id", out var parsedTaskId) ? parsedTaskId : (Guid?)null;
        var scopes = principal.FindFirst("scopes")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (scopes.Length == 0 || scopes.Any(scope => scope.StartsWith("admin", StringComparison.OrdinalIgnoreCase)))
            throw new SecurityTokenException("Agent token scopes are invalid.");
        return new AgentIdentity(tenantId, employeeId, taskId, principal.FindFirst("runner_id")?.Value,
            scopes, principal.FindFirst("jti")?.Value ?? string.Empty,
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("iat")?.Value ?? "0")),
            jwt.ValidTo, credentialEpoch);
    }
}

internal static class ClaimsPrincipalExtensions
{
    public static bool TryGetGuid(this System.Security.Claims.ClaimsPrincipal principal, string type, out Guid value) =>
        Guid.TryParse(principal.FindFirst(type)?.Value, out value);
}
