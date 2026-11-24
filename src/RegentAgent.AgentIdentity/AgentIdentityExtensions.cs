using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RegentAgent.AgentIdentity;

public static class AgentIdentityExtensions
{
    public static IServiceCollection AddAgentIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("AgentIdentity");
        var options = new AgentIdentityOptions
        {
            Issuer = section["Issuer"] ?? "regent-control",
            Audience = section["Audience"] ?? "regent-agent",
            GatewayAudience = section["GatewayAudience"] ?? "regent-gateway",
            LifetimeMinutes = section.GetValue("LifetimeMinutes", 15),
            ClockSkewSeconds = section.GetValue("ClockSkewSeconds", 60),
            SigningKey = section["SigningKey"],
            KeyPath = section["KeyPath"] ?? "data/secrets/agent-token.key"
        };
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton<IAgentIdentityKeyProvider, FileAgentIdentityKeyProvider>();
        services.AddSingleton<AgentAssignmentTokenService>();
        services.AddSingleton<AgentAssignmentTokenValidator>();
        return services;
    }
}
