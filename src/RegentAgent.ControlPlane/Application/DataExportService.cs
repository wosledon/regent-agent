using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Billing;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class DataExportService(RegentDbContext db, JsonDocumentStore<DataExportDto> exports,
    IObjectStore objects, IWebHostEnvironment environment, TimeProvider clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ModuleFiles =
    [
        "workflows.json", "workflow-runs.json", "memory.json", "corrections.json", "memory-vectors.json",
        "memory-findings.json", "memory-impacts.json", "memory-contexts.json", "memory-graph-nodes.json",
        "memory-graph-edges.json", "kpis.json", "reviews.json", "improvements.json", "performance-actions.json",
        "conversations.json", "messages.json", "suites.json", "evaluations.json", "judges.json", "evaluation-qa.json",
        "compliance.json", "localization.json", "project-delivery.json", "governance.json", "lifecycle.json",
        "model-pool.json", "runtime-policy.json", "identity-providers.json", "invitations.json",
        "ownership-delegations.json", "profiles.json", "butler-reports.json", "report-schedules.json",
        "agent-evolution.json", "task-governance.json"
    ];

    public async Task<DataExportDto> CreateAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var id = Guid.NewGuid();
        var database = await ReadDatabaseAsync(tenantId, cancellationToken);
        var modules = ReadModules(tenantId);
        var databaseBytes = JsonSerializer.SerializeToUtf8Bytes(database, JsonOptions);
        var moduleBytes = JsonSerializer.SerializeToUtf8Bytes(modules, JsonOptions);
        using var contentHash = SHA256.Create();
        var contentDigest = Convert.ToHexString(contentHash.ComputeHash(databaseBytes.Concat(moduleBytes).ToArray())).ToLowerInvariant();
        var manifest = new
        {
            schema_version = "1", export_id = id, tenant_id = tenantId, created_at = clock.GetUtcNow(),
            format = "regent-tenant-export", table_count = database.Count, module_count = modules.Count,
            content_sha256 = contentDigest,
            exclusions = new[] { "credentials", "sessions", "recovery-codes", "external-login-transactions", "keyvault", "master-key" }
        };
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, true))
        {
            AddEntry(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
            AddEntry(archive, "database.json", databaseBytes);
            AddEntry(archive, "modules.json", moduleBytes);
        }
        var bytes = package.ToArray();
        var key = $"tenants/{tenantId}/exports/{id:N}.zip";
        await using var stream = new MemoryStream(bytes);
        var stored = await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/zip", bytes.Length), cancellationToken);
        var record = new DataExportDto(id, tenantId, "Completed", "zip", "1", stored.ObjectKey,
            stored.Sha256, stored.Length, database.Count, modules.Count, stored.StoredAt, null, null);
        await exports.PutAsync(id.ToString(), record, cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<DataExportDto>> ListAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        return (await exports.GetAllAsync(cancellationToken)).Where(item => item.TenantId == tenantId)
            .OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async Task<Stream> DownloadAsync(Guid userId, Guid tenantId, Guid exportId, CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var export = await LoadAsync(tenantId, exportId, cancellationToken);
        return await objects.OpenReadAsync(export.ObjectKey, cancellationToken)
            ?? throw new KeyNotFoundException("Export package not found.");
    }

    public async Task<DataExportValidationDto> ValidateAsync(Guid userId, Guid tenantId, Guid exportId,
        CancellationToken cancellationToken)
    {
        await RequireOwnerAsync(userId, tenantId, cancellationToken);
        var export = await LoadAsync(tenantId, exportId, cancellationToken);
        try
        {
            await using var stream = await objects.OpenReadAsync(export.ObjectKey, cancellationToken)
                ?? throw new InvalidOperationException("Export package not found.");
            using var hash = SHA256.Create();
            var digest = Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("Manifest missing.");
            using var manifestStream = manifestEntry.Open();
            using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
            if (manifest.RootElement.GetProperty("tenant_id").GetGuid() != tenantId
                || manifest.RootElement.GetProperty("schema_version").GetString() != export.SchemaVersion)
                throw new InvalidOperationException("Export tenant or schema mismatch.");
            using var databaseStream = (archive.GetEntry("database.json") ?? throw new InvalidOperationException("Database payload missing.")).Open();
            using var modulesStream = (archive.GetEntry("modules.json") ?? throw new InvalidOperationException("Module payload missing.")).Open();
            using var contentHash = SHA256.Create();
            var databaseBytes = await ReadAllAsync(databaseStream, cancellationToken);
            var moduleBytes = await ReadAllAsync(modulesStream, cancellationToken);
            var contentDigest = Convert.ToHexString(contentHash.ComputeHash(databaseBytes.Concat(moduleBytes).ToArray())).ToLowerInvariant();
            if (digest != export.Sha256 || contentDigest != manifest.RootElement.GetProperty("content_sha256").GetString())
                throw new InvalidOperationException("Export checksum mismatch.");
            export = export with { ValidatedAt = clock.GetUtcNow(), ValidationError = null };
            await exports.PutAsync(export.Id.ToString(), export, cancellationToken);
            return new DataExportValidationDto(export.Id, true, digest, export.TableCount, export.ModuleCount, export.ValidatedAt.Value, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or InvalidDataException)
        {
            export = export with { ValidationError = exception.Message };
            await exports.PutAsync(export.Id.ToString(), export, cancellationToken);
            return new DataExportValidationDto(export.Id, false, export.Sha256, export.TableCount, export.ModuleCount, clock.GetUtcNow(), exception.Message);
        }
    }

    private async Task<Dictionary<string, List<Dictionary<string, object?>>>> ReadDatabaseAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var tables = db.Model.GetEntityTypes().Where(entity => entity.FindProperty("TenantId") is not null
                && entity.GetTableName() != "IdempotencyRecords").Select(entity => entity.GetTableName())
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table}\" WHERE \"TenantId\" = @tenant_id";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "tenant_id";
                parameter.Value = tenantId.ToString();
                command.Parameters.Add(parameter);
                var rows = new List<Dictionary<string, object?>>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                    for (var i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                    rows.Add(row);
                }
                result[table!] = rows;
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
        return result;
    }

    private Dictionary<string, object> ReadModules(Guid tenantId)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var root = Path.Combine(environment.ContentRootPath, "data", "modules");
        foreach (var file in ModuleFiles)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                var selected = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals(tenantId.ToString(), StringComparison.OrdinalIgnoreCase)
                        || property.Name.StartsWith(tenantId + ":", StringComparison.OrdinalIgnoreCase)
                        || (property.Value.ValueKind == JsonValueKind.Object
                            && property.Value.TryGetProperty("TenantId", out var value)
                            && value.ValueKind == JsonValueKind.String
                            && value.GetString()?.Equals(tenantId.ToString(), StringComparison.OrdinalIgnoreCase) == true))
                        selected[property.Name] = property.Value.Clone();
                }
                if (selected.Count > 0) result[file] = selected;
            }
            catch (JsonException) { }
        }
        return result;
    }

    private async Task<DataExportDto> LoadAsync(Guid tenantId, Guid exportId, CancellationToken cancellationToken)
    {
        var export = await exports.GetAsync(exportId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException("Export not found.");
        if (export.TenantId != tenantId) throw new UnauthorizedAccessException("Export belongs to another tenant.");
        return export;
    }

    private async Task RequireOwnerAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var role = await db.Memberships.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId)
            .Select(x => x.Role).SingleOrDefaultAsync(cancellationToken);
        if (role != "Owner") throw new UnauthorizedAccessException("Primary Owner permission required.");
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
