using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class SchemaVersionService(RegentDbContext db, TimeProvider clock)
{
    public const int CurrentVersion = 1;

    public async Task ValidateAsync(CancellationToken cancellationToken, bool allowBootstrap = true)
    {
        var latest = await db.SchemaVersions.AsNoTracking().Select(x => (int?)x.Version).MaxAsync(cancellationToken) ?? 0;
        if (latest > CurrentVersion) throw new InvalidOperationException("Database schema is newer than this application.");
        if (latest < CurrentVersion && !allowBootstrap)
            throw new InvalidOperationException("Database schema is missing the required baseline; run the migration job first.");
        if (latest < CurrentVersion)
        {
            db.SchemaVersions.Add(new SchemaVersionRecord
            {
                Version = CurrentVersion, Name = "local-schema-baseline", AppliedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
