using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Projects;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Projects;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class DeliveryArtifactVerifier(RegentDbContext db, IObjectStore objects) : IDeliveryArtifactVerifier
{
    public async Task<IReadOnlyList<string>> ValidateAsync(Guid tenantId, Guid projectId, Guid taskId,
        IReadOnlyList<EvidenceRef> artifacts, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var artifact in artifacts)
        {
            if (!Guid.TryParse(artifact.Id, out var artifactId) || string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                errors.Add("Every artifact must cite its authoritative ID and SHA-256 digest.");
                continue;
            }
            var record = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == artifactId
                && x.TenantId == tenantId && x.ProjectId == projectId && x.TaskId == taskId
                && x.UploadState == "Ready", cancellationToken);
            if (record is null || !string.Equals(record.ObjectKey, artifact.Location, StringComparison.Ordinal)
                || !string.Equals(record.Hash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Artifact ownership, work item, location or digest is invalid.");
                continue;
            }
            await using var stream = await objects.OpenReadAsync(record.ObjectKey, cancellationToken)
                ?? throw new InvalidOperationException("Artifact content is missing.");
            using var hash = SHA256.Create();
            var actual = Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken));
            if (!string.Equals(actual, record.Hash, StringComparison.OrdinalIgnoreCase))
                errors.Add("Artifact content does not match its authoritative digest.");
        }
        return errors;
    }
}
