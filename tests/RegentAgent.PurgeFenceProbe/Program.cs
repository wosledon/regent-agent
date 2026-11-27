using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Storage;

if (args.Length != 4)
{
    Console.Error.WriteLine("Expected root, tenant id, project id, and object root.");
    return 2;
}

var root = Path.GetFullPath(args[0]);
var tenantId = Guid.Parse(args[1]);
var projectId = Guid.Parse(args[2]);
var objectRoot = Path.GetFullPath(args[3]);
var lifecyclePath = Path.Combine(root, "data", "modules", "lifecycle.json");
var connectionString = $"Data Source={Path.Combine(root, "regent.db")}";
var services = new ServiceCollection();
services.AddSingleton<ITenantPurgeStateProvider>(provider => new PersistedTenantPurgeStateProvider(
    provider.GetRequiredService<IServiceScopeFactory>(), lifecyclePath));
services.AddScoped<TenantPurgeWriteInterceptor>();
services.AddDbContext<RegentDbContext>((provider, options) => options.UseSqlite(connectionString)
    .AddInterceptors(provider.GetRequiredService<TenantPurgeWriteInterceptor>()));
await using var provider = services.BuildServiceProvider();
var purgeState = provider.GetRequiredService<ITenantPurgeStateProvider>();
var objects = new TenantPurgeObjectStore(new LocalFileObjectStore(objectRoot), new TenantObjectUploadTracker(), purgeState);

try
{
    await using (var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                 .UseSqlite(connectionString).Options))
    {
        var artifactService = new ArtifactService(db, new ProbeTenantContext(tenantId),
            new LocalFileObjectStore(objectRoot), TimeProvider.System);
        await using var content = new MemoryStream("blocked"u8.ToArray());
        try
        {
            await artifactService.UploadAsync(projectId, null, content, "text/plain", content.Length, CancellationToken.None);
            Console.Error.WriteLine("ArtifactService.UploadAsync unexpectedly succeeded.");
            return 10;
        }
        catch (TenantPurgeInProgressException)
        {
        }
    }

    await using (var content = new MemoryStream("blocked"u8.ToArray()))
    {
        try
        {
            await objects.PutAsync(new ObjectUploadRequest($"tenants/{tenantId}/artifacts/direct", content,
                "text/plain", content.Length), CancellationToken.None);
            Console.Error.WriteLine("Direct object write unexpectedly succeeded.");
            return 13;
        }
        catch (TenantPurgeInProgressException)
        {
        }
    }

    var metadataStore = new JsonDocumentStore<DataLifecycleState>(Path.Combine(root, "metadata.json"), purgeState);
    try
    {
        await metadataStore.PutAsync(tenantId.ToString(), new DataLifecycleState { TenantId = tenantId }, CancellationToken.None);
        Console.Error.WriteLine("Direct JSON metadata write unexpectedly succeeded.");
        return 14;
    }
    catch (TenantPurgeInProgressException)
    {
    }

    await using (var scope = provider.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId,
            ObjectKey = $"tenants/{tenantId}/artifacts/direct-ef", ContentType = "text/plain",
            CreatedAt = DateTimeOffset.UtcNow
        });
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
            Console.Error.WriteLine("Direct EF metadata write unexpectedly succeeded.");
            return 15;
        }
        catch (TenantPurgeInProgressException)
        {
        }
    }

    await using (var scope = provider.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        if (await db.Artifacts.AnyAsync(CancellationToken.None))
        {
            Console.Error.WriteLine("Blocked upload created artifact metadata.");
            return 11;
        }
    }

    if (Directory.Exists(objectRoot) && Directory.EnumerateFiles(objectRoot, "*", SearchOption.AllDirectories).Any())
    {
        Console.Error.WriteLine("Blocked upload created an object.");
        return 12;
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 16;
}

internal sealed class ProbeTenantContext(Guid tenantId) : ITenantContext
{
    public TenantId TenantId { get; } = new(tenantId);
    public UserId UserId { get; } = new(Guid.NewGuid());
    public string DisplayName => "Purge fence probe";
    public bool IsAuthenticated => true;
}
