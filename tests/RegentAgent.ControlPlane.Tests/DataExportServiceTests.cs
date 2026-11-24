using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class DataExportServiceTests
{
    [Fact]
    public async Task Tenant_export_is_checksummed_validated_and_excludes_session_secrets()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        try
        {
            var dbPath = Path.Combine(root, "regent.db");
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={dbPath}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenant, Name = "Export", CompanyName = "Export Co" });
            db.Users.Add(new UserRecord { Id = user, Email = "owner@export.test", DisplayName = "Owner" });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, Role = "Owner" });
            db.Projects.Add(new ProjectRecord
            {
                Id = Guid.NewGuid(), TenantId = tenant, Name = "Exportable project", Owner = "Owner",
                Stage = ProjectStage.Proposed, Health = ProjectHealth.OnTrack, Currency = "CNY",
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            Directory.CreateDirectory(Path.Combine(root, "data", "modules"));
            await File.WriteAllTextAsync(Path.Combine(root, "data", "modules", "sessions.json"),
                "{\"" + tenant + "\":{\"token\":\"must-not-export\"}}");
            var service = new DataExportService(db,
                new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json")),
                new LocalFileObjectStore(Path.Combine(root, "objects")), new TestEnvironment(root), TimeProvider.System);
            var export = await service.CreateAsync(user, tenant, CancellationToken.None);
            Assert.True(export.TableCount > 0);
            Assert.NotEmpty(export.Sha256);
            var validation = await service.ValidateAsync(user, tenant, export.Id, CancellationToken.None);
            Assert.True(validation.Valid, validation.Error);
            await using var download = await service.DownloadAsync(user, tenant, export.Id, CancellationToken.None);
            using var package = new MemoryStream();
            await download.CopyToAsync(package);
            package.Position = 0;
            using var archive = new ZipArchive(package, ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("database.json"));
            Assert.Null(archive.GetEntry("sessions.json"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync(user, Guid.NewGuid(), CancellationToken.None));
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
