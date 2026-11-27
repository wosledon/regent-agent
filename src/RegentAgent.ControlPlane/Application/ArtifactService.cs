using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Artifacts;
using RegentAgent.Contracts.Primitives;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class ArtifactService(RegentDbContext db, ITenantContext tenant, IObjectStore objects, TimeProvider clock,
    ITenantPurgeStateProvider? purgeState = null, string? lifecycleStatePath = null)
{
    private readonly ITenantPurgeStateProvider purgeAdmission = purgeState
        ?? new ArtifactDbContextPurgeStateProvider(db,
            lifecycleStatePath ?? TenantPurgeStateInspector.DefaultLifecycleStatePath);

    public async Task<ArtifactDto> UploadAsync(Guid projectId, Guid? taskId, Stream content,
        string contentType, long length, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        await TenantPurgeFence.CheckPersistedPurgeAsync(tenantId, cancellationToken, purgeAdmission);
        using var writeAccess = await TenantPurgeFence.EnterWriteAsync(tenantId, cancellationToken);
        var projectExists = await db.Projects.AnyAsync(x => x.TenantId == tenantId && x.Id == projectId, cancellationToken);
        if (!projectExists) throw new KeyNotFoundException("Project not found.");
        if (taskId is { } requestedTaskId && !await db.Tasks.AnyAsync(x => x.TenantId == tenantId && x.Id == requestedTaskId && x.ProjectId == projectId, cancellationToken))
        {
            throw new KeyNotFoundException("Task not found in project.");
        }

        var artifactId = Guid.NewGuid();
        var objectKey = $"tenants/{tenantId}/projects/{projectId}/artifacts/{artifactId}";
        var record = new ArtifactRecord
        {
            Id = artifactId, TenantId = tenantId, ProjectId = projectId, TaskId = taskId,
            ObjectKey = objectKey, ContentType = contentType, CreatedAt = clock.GetUtcNow()
        };
        db.Artifacts.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        var stored = await objects.PutAsync(new ObjectUploadRequest(objectKey, content, contentType, length), cancellationToken);
        record.Hash = stored.Sha256;
        record.Length = stored.Length;
        record.UploadState = "Ready";
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(record);
    }

    public async Task<(ArtifactDto Artifact, Stream Content)?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var record = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (record is null || record.UploadState != "Ready") return null;
        var stream = await objects.OpenReadAsync(record.ObjectKey, cancellationToken);
        return stream is null ? null : (ToDto(record), stream);
    }

    public async Task<IReadOnlyList<ArtifactDto>> ListForTaskAsync(Guid projectId, Guid taskId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var records = await db.Artifacts.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProjectId == projectId && x.TaskId == taskId
                && x.UploadState == "Ready")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return records.Select(ToDto).ToArray();
    }

    private static ArtifactDto ToDto(ArtifactRecord record) => new(
        record.Id, record.ProjectId, record.TaskId, record.ObjectKey, record.Hash,
        record.ContentType, record.Length, record.UploadState, record.CreatedAt);

    private sealed class ArtifactDbContextPurgeStateProvider(RegentDbContext db, string lifecycleStatePath)
        : ITenantPurgeStateProvider
    {
        public Task<bool> IsPurgeActiveAsync(Guid tenantId, CancellationToken cancellationToken) =>
            TenantPurgeStateInspector.IsPurgeActiveAsync(db, tenantId, lifecycleStatePath, cancellationToken);
    }
}
