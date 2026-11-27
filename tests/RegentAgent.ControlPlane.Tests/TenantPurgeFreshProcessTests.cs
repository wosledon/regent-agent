using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TenantPurgeFreshProcessTests
{
    [Theory]
    [InlineData("tenant-status")]
    [InlineData("lifecycle-purging")]
    [InlineData("lifecycle-purged")]
    public async Task Fresh_process_direct_artifact_construction_rejects_upload_without_metadata_or_object(
        string persistedState)
    {
        var root = Path.Combine(Path.GetTempPath(), $"purge-fresh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var connectionString = $"Data Source={Path.Combine(root, "regent.db")}";
            await using (var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                         .UseSqlite(connectionString).Options))
            {
                await db.Database.EnsureCreatedAsync();
                db.Tenants.Add(new TenantRecord
                {
                    Id = tenantId, Name = "fresh-process", CompanyName = "Fresh Process Co",
                    Status = persistedState == "tenant-status" ? "Purging" : "Active"
                });
                db.Projects.Add(new ProjectRecord
                {
                    Id = projectId, TenantId = tenantId, Name = "Blocked project", Mission = "Synthetic",
                    Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
                    TargetAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync();
            }

            if (persistedState != "tenant-status")
            {
                var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(
                    Path.Combine(root, "data", "modules", "lifecycle.json"));
                var state = new DataLifecycleState { TenantId = tenantId };
                if (persistedState == "lifecycle-purging")
                {
                    state.DeletionRequests.Add(new DeletionRequestRecord
                    {
                        TenantId = tenantId, ResourceType = "Tenant", ResourceId = tenantId.ToString(),
                        Status = "Purging", DeletedAt = DateTimeOffset.UtcNow, RestoreUntil = DateTimeOffset.UtcNow
                    });
                }
                else
                {
                    state.DeletionProofs.Add(new DeletionProof
                    {
                        ResourceType = "Tenant", ResourceId = tenantId.ToString(),
                        PurgedAt = DateTimeOffset.UtcNow
                    });
                }
                await lifecycleStore.PutAsync(tenantId.ToString(), state, CancellationToken.None);
            }

            var configuration = typeof(TenantPurgeFreshProcessTests).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
            var probe = Path.Combine(PostgresTestSupport.RepositoryRoot, "tests", "RegentAgent.PurgeFenceProbe",
                "bin", configuration, "net10.0", "RegentAgent.PurgeFenceProbe.dll");
            Assert.True(File.Exists(probe), $"Purge fence probe was not built at {probe}.");
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrWhiteSpace(dotnet)) dotnet = "dotnet";
            var startInfo = new ProcessStartInfo(dotnet)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = root
            };
            startInfo.ArgumentList.Add(probe);
            startInfo.ArgumentList.Add(root);
            startInfo.ArgumentList.Add(tenantId.ToString());
            startInfo.ArgumentList.Add(projectId.ToString());
            startInfo.ArgumentList.Add(Path.Combine(root, "objects"));

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Probe process did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Purge fence probe timed out.");
            }

            Assert.True(process.ExitCode == 0,
                $"Probe exited with {process.ExitCode}. stdout: {await stdout} stderr: {await stderr}");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
