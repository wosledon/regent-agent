using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Governance;
using RegentAgent.Contracts.Memory;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("Postgres runtime")]
public sealed class PostgresTenantPurgeTests
{
    [PostgresFact]
    public async Task Postgres_purge_cleans_all_tenant_families_and_retries_after_relational_delete()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertSupportedVersionAsync(connectionString);
        var root = Path.Combine(Path.GetTempPath(), $"postgres-purge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var lifecyclePath = Path.Combine(root, "data", "modules", "lifecycle.json");
        var objectRoot = Path.Combine(root, "objects");
        var accessor = new HttpContextAccessor();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(accessor);
        services.AddSingleton<ITenantPurgeStateProvider>(provider => new PersistedTenantPurgeStateProvider(
            provider.GetRequiredService<IServiceScopeFactory>(), lifecyclePath));
        services.AddScoped<TenantPurgeWriteInterceptor>();
        services.AddDbContext<RegentDbContext>((provider, options) => options.UseNpgsql(connectionString)
            .AddInterceptors(new TenantRlsConnectionInterceptor(accessor))
            .AddInterceptors(provider.GetRequiredService<TenantPurgeWriteInterceptor>()));
        await using var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var purgeState = serviceProvider.GetRequiredService<ITenantPurgeStateProvider>();
        var uploads = new TenantObjectUploadTracker();
        var objects = new FailOnceObjectStore(new TenantPurgeObjectStore(
            new LocalFileObjectStore(objectRoot), uploads, purgeState));
        var exports = new JsonDocumentStore<DataExportDto>(Path.Combine(root, "data", "modules", "exports.json"), purgeState);
        var moduleStore = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "data", "modules", "memory.json"), purgeState);
        var lifecycleStore = new JsonDocumentStore<DataLifecycleState>(lifecyclePath, purgeState);
        await using var vectors = new PostgresMemoryVectorStore(connectionString);
        var clock = new MutableTimeProvider();
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        try
        {
            var schemaVersionCountBefore = await ReadSchemaVersionCountAsync(scopeFactory, accessor);
            var seeded = await SeedTenantAsync(scopeFactory, accessor, tenantId, "purge-target", objects, exports,
                moduleStore, vectors);
            var retained = await SeedTenantAsync(scopeFactory, accessor, otherTenantId, "purge-retained", objects,
                exports, moduleStore, vectors);

            using (var scope = scopeFactory.CreateAsyncScope())
            {
                SetTenantContext(accessor, tenantId);
                var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
                var purge = new TenantPurgeService(db, objects, [moduleStore], vectors, exports, lifecycleStore, clock, uploads);
                var lifecycle = new DataLifecycleService(lifecycleStore, objects, db, clock, purge, new TenantRlsScope(db));
                var request = await lifecycle.SoftDeleteAsync(tenantId, "Tenant", tenantId.ToString(), "Owner requested",
                    CancellationToken.None);
                clock.Advance(TimeSpan.FromDays(8));
                objects.FailNextDeletePrefix = true;

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    lifecycle.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None));

                Assert.False(await db.Tenants.AnyAsync(item => item.Id == tenantId));
                Assert.Empty(await db.Projects.Where(item => item.TenantId == tenantId).AsNoTracking().ToListAsync());
                Assert.False(await objects.IsPrefixEmptyAsync($"tenants/{tenantId}/", CancellationToken.None));
                Assert.Single(await vectors.GetAllAsync(tenantId.ToString("D"), CancellationToken.None));
                Assert.NotNull(await moduleStore.GetAsync(seeded.ClaimId, CancellationToken.None));
                Assert.NotNull(await exports.GetAsync(seeded.ExportId.ToString(), CancellationToken.None));
                var pending = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
                Assert.NotNull(pending);
                Assert.True(pending.IsPurging);
                Assert.Empty(pending.DeletionProofs);

                objects.FailNextDeletePrefix = false;
                var proof = await lifecycle.PurgeAsync(tenantId, request.Id, "PURGE", CancellationToken.None);

                Assert.False(string.IsNullOrWhiteSpace(proof.ProofHash));
                var completed = await lifecycleStore.GetAsync(tenantId.ToString(), CancellationToken.None);
                Assert.NotNull(completed);
                Assert.True(completed.IsPurged);
                Assert.Equal(proof.Id, Assert.Single(completed.DeletionProofs).Id);
                Assert.Empty(completed.DeletionRequests);
                Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId}/", CancellationToken.None));
                Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId:N}/", CancellationToken.None));
                Assert.Empty(await vectors.GetAllAsync(tenantId.ToString("D"), CancellationToken.None));
                Assert.Null(await moduleStore.GetAsync(seeded.ClaimId, CancellationToken.None));
                Assert.Null(await exports.GetAsync(seeded.ExportId.ToString(), CancellationToken.None));
            }

            await AssertRelationalTenantAsync(scopeFactory, accessor, tenantId, purged: true);
            await AssertRelationalTenantAsync(scopeFactory, accessor, otherTenantId, purged: false);
            var schemaVersionCountAfter = await ReadSchemaVersionCountAsync(scopeFactory, accessor);
            Assert.Equal(schemaVersionCountBefore, schemaVersionCountAfter);
            Assert.True(await UserExistsAsync(scopeFactory, accessor, seeded.UserId));
            Assert.True(await UserExistsAsync(scopeFactory, accessor, retained.UserId));
            Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId}/", CancellationToken.None));
            Assert.True(await objects.IsPrefixEmptyAsync($"tenants/{tenantId:N}/", CancellationToken.None));
            Assert.False(await objects.IsPrefixEmptyAsync($"tenants/{otherTenantId}/", CancellationToken.None));
            foreach (var key in seeded.ObjectKeys) Assert.Null(await objects.GetMetadataAsync(key, CancellationToken.None));
            foreach (var key in retained.ObjectKeys) Assert.NotNull(await objects.GetMetadataAsync(key, CancellationToken.None));
            Assert.Empty(await vectors.GetAllAsync(tenantId.ToString("D"), CancellationToken.None));
            Assert.Single(await vectors.GetAllAsync(otherTenantId.ToString("D"), CancellationToken.None));
            Assert.Null(await moduleStore.GetAsync(seeded.ClaimId, CancellationToken.None));
            Assert.NotNull(await moduleStore.GetAsync(retained.ClaimId, CancellationToken.None));
            Assert.Null(await exports.GetAsync(seeded.ExportId.ToString(), CancellationToken.None));
            Assert.NotNull(await exports.GetAsync(retained.ExportId.ToString(), CancellationToken.None));
        }
        finally
        {
            objects.FailNextDeletePrefix = false;
            await CleanupTenantAsync(scopeFactory, accessor, tenantId, objects, moduleStore, vectors, exports,
                lifecycleStore, clock, uploads);
            await CleanupTenantAsync(scopeFactory, accessor, otherTenantId, objects, moduleStore, vectors, exports,
                lifecycleStore, clock, uploads);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<int> ReadSchemaVersionCountAsync(IServiceScopeFactory scopeFactory,
        IHttpContextAccessor accessor)
    {
        using var scope = scopeFactory.CreateAsyncScope();
        SetTenantContext(accessor, Guid.Empty);
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        return await db.SchemaVersions.CountAsync();
    }

    private static async Task<bool> UserExistsAsync(IServiceScopeFactory scopeFactory,
        IHttpContextAccessor accessor, Guid userId)
    {
        using var scope = scopeFactory.CreateAsyncScope();
        SetTenantContext(accessor, Guid.Empty);
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        return await db.Users.AnyAsync(user => user.Id == userId);
    }

    private static async Task<SeededTenant> SeedTenantAsync(IServiceScopeFactory scopeFactory,
        IHttpContextAccessor accessor, Guid tenantId, string name, IObjectStore objects,
        JsonDocumentStore<DataExportDto> exports, JsonDocumentStore<MemoryClaim> moduleStore,
        IMemoryVectorStore vectors)
    {
        using var scope = scopeFactory.CreateAsyncScope();
        SetTenantContext(accessor, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var userId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var milestoneId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var exportId = Guid.NewGuid();
        var claimId = $"claim-{tenantId:N}";
        var now = DateTimeOffset.UtcNow;
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = name, CompanyName = $"{name} Co" });
        db.Users.Add(new UserRecord
        {
            Id = userId, Email = $"{name}-{userId:N}@example.test", DisplayName = $"{name} owner"
        });
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = "Owner"
        });
        db.Charters.Add(new CharterRecord { TenantId = tenantId, Mission = "Synthetic", UpdatedAt = now });
        db.Positions.Add(new PositionRecord
        {
            Id = positionId, TenantId = tenantId, Name = "Engineer", Responsibilities = "Build",
            Path = PositionPath.Root(positionId)
        });
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = $"EMP-{tenantId:N}", DisplayName = "Employee",
            Position = "Engineer"
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, PositionId = positionId,
            Kind = "Primary", StartedAt = now
        });
        db.OrgDelegations.Add(new OrgDelegationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, DelegatorEmployeeId = employeeId,
            DelegateEmployeeId = employeeId, SubtreePositionId = positionId, ExpiresAt = now.AddDays(1)
        });
        db.Candidates.Add(new CandidateRecord
        {
            Id = candidateId, TenantId = tenantId, PositionId = positionId, DisplayName = "Candidate",
            CreatedAt = now
        });
        db.InterviewStages.Add(new InterviewStageRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CandidateId = candidateId, Stage = "Screen",
            Status = "Pending", CreatedAt = now
        });
        db.CandidateAppeals.Add(new CandidateAppealRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CandidateId = candidateId, Reason = "Synthetic",
            Status = "Pending"
        });
        db.FairnessAudits.Add(new FairnessAuditRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PositionId = positionId, Period = "2026-11",
            SampleSize = 1, SelectionRateRatio = 1m, Status = "Passed"
        });
        db.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = $"{name} project", Mission = "Synthetic",
            Owner = "Owner", Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack,
            TargetAt = now, UpdatedAt = now
        });
        db.Milestones.Add(new MilestoneRecord
        {
            Id = milestoneId, TenantId = tenantId, ProjectId = projectId, Name = "Milestone",
            TargetAt = now, Status = "Pending", AcceptanceRule = "Synthetic"
        });
        db.Tasks.Add(new TaskRecord
        {
            Id = taskId, TenantId = tenantId, ProjectId = projectId, Title = "Task", TaskType = "work",
            Status = "Pending", Assignee = "Employee", AssigneeEmployeeId = employeeId, UpdatedAt = now
        });
        db.TaskCoordinations.Add(new TaskCoordinationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = taskId, ProjectId = projectId,
            Kind = "Review", Status = "Pending", RequesterEmployeeId = employeeId,
            TargetEmployeeId = employeeId, Summary = "Synthetic", SortKey = "synthetic",
            CreatedAt = now, UpdatedAt = now
        });
        db.WorkEntries.Add(new WorkEntryRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, TaskId = taskId,
            OccurredAt = now, Title = "Work", EventType = "synthetic", Status = "Recorded"
        });
        db.Approvals.Add(new ApprovalRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActionType = "synthetic", Title = "Approval",
            Impact = "Synthetic", Severity = Severity.Low, State = ApprovalState.Pending,
            Requester = "Owner", RequesterUserId = userId, CreatedAt = now, ExpiresAt = now.AddDays(1)
        });
        db.AuditRecords.Add(new AuditRecord
        {
            TenantId = tenantId, Seq = 1, EventType = "synthetic.purge", Actor = "test",
            PayloadJson = "{}", PreviousHash = "", Hash = "synthetic", CreatedAt = now
        });
        db.AuditCheckpoints.Add(new AuditCheckpointRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ThroughSeq = 1, MerkleRoot = "synthetic", CreatedAt = now
        });
        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Key = "key", Operation = "synthetic",
            RequestHash = "hash", CreatedAt = now
        });
        db.Outbox.Add(new OutboxRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EventType = "synthetic", PayloadJson = "{}",
            Status = "Processed", ProcessedAt = now, CreatedAt = now
        });
        db.Inbox.Add(new InboxRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, MessageId = Guid.NewGuid(), EventType = "synthetic",
            PayloadJson = "{}", CreatedAt = now
        });
        var artifactKey = $"tenants/{tenantId}/projects/{projectId}/artifacts/{artifactId}";
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = artifactId, TenantId = tenantId, ProjectId = projectId, ObjectKey = artifactKey,
            Hash = "synthetic", ContentType = "text/plain", Length = 1, UploadState = "Ready", CreatedAt = now
        });
        db.Notifications.Add(new NotificationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RecipientId = userId, Level = "Info",
            Title = "Synthetic", CreatedAt = now
        });
        await db.SaveChangesAsync();

        string[] objectKeys =
        [
            $"tenants/{tenantId}/archives/2026/archive/{Guid.NewGuid():N}.json",
            $"tenants/{tenantId}/backups/{Guid.NewGuid():N}.json",
            artifactKey,
            $"tenants/{tenantId}/exports/{exportId:N}.zip",
            $"tenants/{tenantId}/misc/orphan-{Guid.NewGuid():N}",
            $"tenants/{tenantId:N}/audit-anchors/{Guid.NewGuid():N}.json"
        ];
        StoredObject? exportObject = null;
        foreach (var key in objectKeys)
        {
            await using var content = new MemoryStream("synthetic"u8.ToArray());
            var stored = await objects.PutAsync(new ObjectUploadRequest(key, content, "application/octet-stream"),
                CancellationToken.None);
            if (key.EndsWith(".zip", StringComparison.Ordinal)) exportObject = stored;
        }
        await exports.PutAsync(exportId.ToString(), new DataExportDto(exportId, tenantId, "Completed", "zip", "1",
            objectKeys[3], exportObject!.Sha256, exportObject.Length, 1, 1, exportObject.StoredAt, null, null),
            CancellationToken.None);
        await moduleStore.PutAsync(claimId, new MemoryClaim(claimId, tenantId.ToString(), "company", "synthetic",
            "source", 1, "trusted", "Active", 1), CancellationToken.None);
        await vectors.PutAsync(new MemoryVectorRecord(claimId, tenantId.ToString("D"), "hash", "test-v1",
            LocalEmbeddingProvider.Embed("synthetic"), now), CancellationToken.None);
        return new SeededTenant(userId, exportId, claimId, objectKeys);
    }

    private static async Task AssertRelationalTenantAsync(IServiceScopeFactory scopeFactory,
        IHttpContextAccessor accessor, Guid tenantId, bool purged)
    {
        using var scope = scopeFactory.CreateAsyncScope();
        SetTenantContext(accessor, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        Assert.Equal(!purged, await db.Tenants.AnyAsync(item => item.Id == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Memberships.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Charters.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Positions.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.EmploymentPeriods.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.OrgDelegations.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Candidates.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.InterviewStages.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.CandidateAppeals.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.FairnessAudits.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Employees.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Projects.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Milestones.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Tasks.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.TaskCoordinations.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.WorkEntries.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Approvals.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(1, await db.AuditRecords.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(1, await db.AuditCheckpoints.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.IdempotencyRecords.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Outbox.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Inbox.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Artifacts.CountAsync(item => item.TenantId == tenantId));
        Assert.Equal(purged ? 0 : 1, await db.Notifications.CountAsync(item => item.TenantId == tenantId));
    }

    private static async Task CleanupTenantAsync(IServiceScopeFactory scopeFactory, IHttpContextAccessor accessor,
        Guid tenantId, IObjectStore objects, JsonDocumentStore<MemoryClaim> moduleStore, IMemoryVectorStore vectors,
        JsonDocumentStore<DataExportDto> exports, JsonDocumentStore<DataLifecycleState> lifecycleStore,
        TimeProvider clock, TenantObjectUploadTracker uploads)
    {
        using var scope = scopeFactory.CreateAsyncScope();
        SetTenantContext(accessor, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var purge = new TenantPurgeService(db, objects, [moduleStore], vectors, exports, lifecycleStore, clock, uploads);
        await purge.PurgeAsync(tenantId, CancellationToken.None);
    }

    private static void SetTenantContext(IHttpContextAccessor accessor, Guid tenantId)
    {
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                tenantId == Guid.Empty ? [] : [new Claim("tenant_id", tenantId.ToString("D"))], "test"))
        };
    }

    private sealed record SeededTenant(Guid UserId, Guid ExportId, string ClaimId, string[] ObjectKeys);

    private sealed class FailOnceObjectStore(IObjectStore inner) : IObjectStore
    {
        public bool FailNextDeletePrefix { get; set; }
        public Task<StoredObject> PutAsync(ObjectUploadRequest request, CancellationToken cancellationToken) =>
            inner.PutAsync(request, cancellationToken);
        public Task<StoredObject?> GetMetadataAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.GetMetadataAsync(objectKey, cancellationToken);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.OpenReadAsync(objectKey, cancellationToken);
        public Task<bool> DeleteAsync(string objectKey, CancellationToken cancellationToken) =>
            inner.DeleteAsync(objectKey, cancellationToken);
        public Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken)
        {
            if (!FailNextDeletePrefix) return inner.DeletePrefixAsync(prefix, cancellationToken);
            FailNextDeletePrefix = false;
            throw new InvalidOperationException("Injected failure after relational delete.");
        }
        public Task<bool> IsPrefixEmptyAsync(string prefix, CancellationToken cancellationToken) =>
            inner.IsPrefixEmptyAsync(prefix, cancellationToken);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
