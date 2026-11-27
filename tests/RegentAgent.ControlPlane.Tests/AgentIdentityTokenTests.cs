using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using RegentAgent.AgentIdentity;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class AgentIdentityTokenTests
{
    private readonly AgentIdentityOptions options = new()
    {
        SigningKey = new string('s', 32), LifetimeMinutes = 15, ClockSkewSeconds = 0
    };
    private readonly IAgentIdentityKeyProvider keys = new FixedKeyProvider();

    [Fact]
    public void Employee_token_binds_tenant_employee_task_and_scopes()
    {
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var credentialEpoch = Guid.NewGuid();
        var service = new AgentAssignmentTokenService(keys, options);
        var issued = service.Issue(new AgentAssignmentRequest(tenantId, employeeId, taskId, "runner-1",
            ["tasks:execute", "chat:write", "infer:employee"], credentialEpoch));

        var identity = new AgentAssignmentTokenValidator(keys, options).Validate(issued.Token);
        var gatewayIdentity = new AgentAssignmentTokenValidator(keys, options).Validate(issued.Token, "regent-gateway");

        Assert.Equal(tenantId, identity.TenantId);
        Assert.Equal(employeeId, identity.EmployeeId);
        Assert.Equal(taskId, identity.TaskId);
        Assert.Equal("runner-1", identity.RunnerId);
        Assert.Equal(credentialEpoch, identity.CredentialEpoch);
        Assert.Contains("infer:employee", identity.Scopes);
        Assert.Equal(employeeId, gatewayIdentity.EmployeeId);
    }

    [Fact]
    public void Token_rejects_wrong_audience_tampering_and_expiry()
    {
        var service = new AgentAssignmentTokenService(keys, options);
        var issued = service.Issue(new AgentAssignmentRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ["tasks:execute"], Guid.NewGuid()));
        var validator = new AgentAssignmentTokenValidator(keys, options);
        Assert.ThrowsAny<SecurityTokenException>(() => validator.Validate(issued.Token, "regent-ops"));
        Assert.ThrowsAny<SecurityTokenException>(() => validator.Validate(issued.Token[..^4] + "AAAA"));

        var expired = new JwtSecurityToken(options.Issuer, options.Audience,
            [
                new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString()),
                new System.Security.Claims.Claim("tenant_id", Guid.NewGuid().ToString()),
                new System.Security.Claims.Claim("employee_id", Guid.NewGuid().ToString()),
                new System.Security.Claims.Claim("scopes", "tasks:execute"),
                new System.Security.Claims.Claim("jti", Guid.NewGuid().ToString("N")),
                new System.Security.Claims.Claim("iat", DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds().ToString(), System.Security.Claims.ClaimValueTypes.Integer64)
            ], DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1),
            new SigningCredentials(new SymmetricSecurityKey(keys.GetSigningKey()), SecurityAlgorithms.HmacSha256));
        var expiredToken = new JwtSecurityTokenHandler().WriteToken(expired);
        Assert.ThrowsAny<SecurityTokenException>(() => validator.Validate(expiredToken));
    }

    [Fact]
    public void Employee_token_cannot_issue_admin_scopes()
    {
        var service = new AgentAssignmentTokenService(keys, options);
        Assert.Throws<InvalidOperationException>(() => service.Issue(new AgentAssignmentRequest(
            Guid.NewGuid(), Guid.NewGuid(), null, null, ["admin:write"], Guid.NewGuid())));
    }

    [Fact]
    public void Employee_token_requires_a_credential_epoch()
    {
        var service = new AgentAssignmentTokenService(keys, options);
        Assert.Throws<InvalidOperationException>(() => service.Issue(new AgentAssignmentRequest(
            Guid.NewGuid(), Guid.NewGuid(), null, null, ["tasks:execute"])));
    }

    private sealed class FixedKeyProvider : IAgentIdentityKeyProvider
    {
        public byte[] GetSigningKey() => Enumerable.Repeat((byte)'s', 32).ToArray();
    }
}
