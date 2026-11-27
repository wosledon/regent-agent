using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("ControlPlane HTTP security", DisableParallelization = true)]
public sealed class ControlPlaneSecurityHttpCollection;

internal sealed class ControlPlaneSecurityHttpFactory : ControlPlaneRuntimeFactory
{
    public ControlPlaneSecurityHttpFactory() : base("Development", "InMemory", true)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
