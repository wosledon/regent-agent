extern alias RegentAgentControlPlane;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Storage;
using ControlPlaneProgram = RegentAgentControlPlane::Program;

namespace RegentAgent.ControlPlane.Tests;

internal class ControlPlaneRuntimeFactory : WebApplicationFactory<ControlPlaneProgram>
{
    private readonly string environment;
    private readonly string storageProvider;
    private readonly bool useInMemory;
    private readonly string? postgresConnectionString;
    private readonly string previousCurrentDirectory = Directory.GetCurrentDirectory();
    public string Root { get; }

    public ControlPlaneRuntimeFactory(string environment, string storageProvider, bool useInMemory,
        string? postgresConnectionString = null)
    {
        this.environment = environment;
        this.storageProvider = storageProvider;
        this.useInMemory = useInMemory;
        this.postgresConnectionString = postgresConnectionString;
        TenantPurgeFence.Reset(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Root = Path.Combine(Path.GetTempPath(), $"regent-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
        Directory.SetCurrentDirectory(Root);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(FindContentRoot());
        builder.UseEnvironment(environment);
        builder.UseSetting("Storage:Provider", storageProvider);
        builder.UseSetting("Storage:UseInMemory", useInMemory.ToString());
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={Path.Combine(Root, "regent.db")}");
        if (postgresConnectionString is not null)
            builder.UseSetting("ConnectionStrings:Postgres", postgresConnectionString);
        builder.UseSetting("Storage:ObjectProvider", "Local");
        builder.UseSetting("Storage:ObjectRoot", Path.Combine(Root, "objects"));
        builder.UseSetting("Data:OperationsPath", Path.Combine(Root, "ops"));
        builder.UseSetting("AgentIdentity:KeyPath", Path.Combine(Root, "secrets", "agent-token.key"));
        builder.UseSetting("Audit:AnchorProvider", "Local");
        builder.UseSetting("Observability:Profile", "off");
        builder.UseSetting("Identity:AllowDemoLogin", "false");
        builder.UseSetting("Runner:Token", "controlplane-tests-only");
        builder.UseSetting("Metering:Token", "controlplane-tests-only");
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            if (disposing)
            {
                Directory.SetCurrentDirectory(previousCurrentDirectory);
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static string FindContentRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "RegentAgent.ControlPlane");
            if (File.Exists(Path.Combine(candidate, "RegentAgent.ControlPlane.csproj"))) return candidate;
        }
        return "/app";
    }
}

internal sealed class RuntimeTestTenantContext(Guid tenantId) : ITenantContext
{
    public TenantId TenantId { get; } = new(tenantId);
    public UserId UserId { get; } = new(Guid.NewGuid());
    public string DisplayName => "Synthetic Owner";
    public bool IsAuthenticated => true;
}

internal sealed class RuntimeTestQuotaGuard : ITenantQuotaGuard
{
    public Task EnsureAsync(Guid tenantId, string resource, int increment, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
