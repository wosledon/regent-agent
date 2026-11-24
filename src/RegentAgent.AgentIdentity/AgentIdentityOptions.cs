using System.Text;

namespace RegentAgent.AgentIdentity;

public sealed class AgentIdentityOptions
{
    public string Issuer { get; set; } = "regent-control";
    public string Audience { get; set; } = "regent-agent";
    public string GatewayAudience { get; set; } = "regent-gateway";
    public int LifetimeMinutes { get; set; } = 15;
    public int ClockSkewSeconds { get; set; } = 60;
    public string? SigningKey { get; set; }
    public string KeyPath { get; set; } = "data/secrets/agent-token.key";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || string.IsNullOrWhiteSpace(GatewayAudience))
            throw new InvalidOperationException("Agent token issuer and audience are required.");
        if (LifetimeMinutes is < 1 or > 60) throw new InvalidOperationException("Agent token lifetime is invalid.");
        if (ClockSkewSeconds is < 0 or > 300) throw new InvalidOperationException("Agent token clock skew is invalid.");
        if (!string.IsNullOrEmpty(SigningKey) && Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Agent token signing key must contain at least 32 bytes.");
    }
}
