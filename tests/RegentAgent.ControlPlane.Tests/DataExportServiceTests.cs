using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Memory;
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
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var objects = new NonSeekableObjectStore(new LocalFileObjectStore(Path.Combine(root, "objects")));
            var service = new DataExportService(db, exports, objects, new TestEnvironment(root), TimeProvider.System);
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

            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [exports], vectors, exports);
            await purge.PurgeAsync(tenant, CancellationToken.None);
            await purge.PurgeAsync(tenant, CancellationToken.None);

            Assert.Null(await exports.GetAsync(export.Id.ToString(), CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(export.ObjectKey, CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenant, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Object_put_racing_purge_leaves_no_export_metadata_or_object()
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
            db.Tenants.Add(new TenantRecord { Id = tenant, Name = "Export Race", CompanyName = "Export Race Co" });
            db.Users.Add(new UserRecord { Id = user, Email = "owner@race.test", DisplayName = "Owner" });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, Role = "Owner" });
            await db.SaveChangesAsync();
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var innerObjects = new NonSeekableObjectStore(new LocalFileObjectStore(Path.Combine(root, "objects")));
            var objects = new BlockingObjectStore(innerObjects);
            var service = new DataExportService(db, exports, objects, new TestEnvironment(root), TimeProvider.System);
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [exports], vectors, exports);
            var orphanKey = $"tenants/{tenant}/exports/orphan.zip";
            await using (var orphan = new MemoryStream("orphan"u8.ToArray()))
                await innerObjects.PutAsync(new ObjectUploadRequest(orphanKey, orphan, "application/zip"), CancellationToken.None);

            var createTask = service.CreateAsync(user, tenant, CancellationToken.None);
            await objects.PutStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var purgeTask = purge.PurgeAsync(tenant, CancellationToken.None);
            objects.AllowPut.TrySetResult();

            var export = await createTask;
            await purgeTask;

            Assert.Null(await exports.GetAsync(export.Id.ToString(), CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(export.ObjectKey, CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(orphanKey, CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenant, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Failed_validation_racing_purge_never_recreates_completed_metadata()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "regent.db")}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenant, Name = "Validation Race", CompanyName = "Validation Race Co" });
            db.Users.Add(new UserRecord { Id = user, Email = "owner@validation-race.test", DisplayName = "Owner" });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, Role = "Owner" });
            await db.SaveChangesAsync();
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var innerObjects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var objects = new BlockingReadObjectStore(innerObjects);
            var service = new DataExportService(db, exports, objects, new TestEnvironment(root), TimeProvider.System);
            var export = await service.CreateAsync(user, tenant, CancellationToken.None);
            await innerObjects.DeleteAsync(export.ObjectKey, CancellationToken.None);
            await using (var corrupt = new MemoryStream("corrupt"u8.ToArray()))
                await innerObjects.PutAsync(new ObjectUploadRequest(export.ObjectKey, corrupt, "application/zip"), CancellationToken.None);
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [exports], vectors, exports);

            var validationTask = service.ValidateAsync(user, tenant, export.Id, CancellationToken.None);
            await objects.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, TenantPurgeFence.GetActiveWrites(tenant));
            var purgeTask = purge.PurgeAsync(tenant, CancellationToken.None);
            objects.AllowRead.TrySetResult();

            var validation = await validationTask;
            await purgeTask;
            Assert.False(validation.Valid);
            Assert.Null(await exports.GetAsync(export.Id.ToString(), CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(export.ObjectKey, CancellationToken.None));
            Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenant, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Validation_started_after_durable_purge_is_rejected_without_metadata()
    {
        var root = NewRoot();
        Directory.CreateDirectory(root);
        try
        {
            await using var db = new RegentDbContext(new DbContextOptionsBuilder<RegentDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "regent.db")}").Options);
            await db.Database.EnsureCreatedAsync();
            var tenant = Guid.NewGuid();
            var user = Guid.NewGuid();
            db.Tenants.Add(new TenantRecord { Id = tenant, Name = "Blocked Validation", CompanyName = "Blocked Validation Co" });
            db.Users.Add(new UserRecord { Id = user, Email = "owner@blocked-validation.test", DisplayName = "Owner" });
            db.Memberships.Add(new MembershipRecord { Id = Guid.NewGuid(), TenantId = tenant, UserId = user, Role = "Owner" });
            await db.SaveChangesAsync();
            var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "exports.json"));
            var innerObjects = new LocalFileObjectStore(Path.Combine(root, "objects"));
            var objects = new BlockingDeletePrefixObjectStore(innerObjects);
            var service = new DataExportService(db, exports, objects, new TestEnvironment(root), TimeProvider.System);
            var export = await service.CreateAsync(user, tenant, CancellationToken.None);
            var vectors = new JsonMemoryVectorStore(new JsonDocumentStore<MemoryVectorRecord>(Path.Combine(root, "vectors.json")));
            var purge = new TenantPurgeService(db, objects, [exports], vectors, exports);

            var purgeTask = purge.PurgeAsync(tenant, CancellationToken.None);
            await objects.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TenantPurgeInProgressException>(() =>
                service.ValidateAsync(user, tenant, export.Id, CancellationToken.None));
            objects.AllowDelete.TrySetResult();
            await purgeTask;

            Assert.Null(await exports.GetAsync(export.Id.ToString(), CancellationToken.None));
            Assert.Null(await objects.GetMetadataAsync(export.ObjectKey, CancellationToken.None));
        }
        finally { Delete(root); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private sealed class BlockingObjectStore(IObjectStore inner) : IObjectStore
    {
        private int putCalls;
        public TaskCompletionSource PutStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPut { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref putCalls) != 1) return inner.PutAsync(request, cancellationToken);
            PutStarted.TrySetResult();
            return PutAfterReleaseAsync(request, cancellationToken);
        }

        private async Task<StoredObject> PutAfterReleaseAsync(ObjectUploadRequest request, CancellationToken cancellationToken)
        {
            await AllowPut.Task.WaitAsync(cancellationToken);
            return await inner.PutAsync(request, cancellationToken);
        }

        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
            inner.DeletePrefixAsync(prefix, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class BlockingReadObjectStore(IObjectStore inner) : IObjectStore
    {
        private int readCalls;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref readCalls) != 1) return await inner.OpenReadAsync(objectKey, cancellationToken);
            ReadStarted.TrySetResult();
            await AllowRead.Task.WaitAsync(cancellationToken);
            return await inner.OpenReadAsync(objectKey, cancellationToken);
        }

        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
            inner.DeletePrefixAsync(prefix, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class BlockingDeletePrefixObjectStore(IObjectStore inner) : IObjectStore
    {
        private int deleteCalls;
        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref deleteCalls) != 1) return await inner.DeletePrefixAsync(prefix, cancellationToken);
            DeleteStarted.TrySetResult();
            await AllowDelete.Task.WaitAsync(cancellationToken);
            return await inner.DeletePrefixAsync(prefix, cancellationToken);
        }

        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class NonSeekableObjectStore(IObjectStore inner) : IObjectStore
    {
        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public async Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
        {
            var stream = await inner.OpenReadAsync(objectKey, cancellationToken);
            return stream is null ? null : new NonSeekableStream(stream);
        }
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken) =>
            inner.DeletePrefixAsync(prefix, cancellationToken);
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

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
