using System.Security.Cryptography;
using System.Text;
using Npgsql;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;

namespace RegentAgent.ControlPlane.Tests;

internal sealed record SchemaCommentCoverage(string ObjectType, long Total, long Commented, long Missing,
    decimal CoveragePercent, string MissingObjects);

internal sealed class PostgresTemporarySchema : IAsyncDisposable
{
    private readonly string migrationConnectionString;

    private PostgresTemporarySchema(string migrationConnectionString, string name)
    {
        this.migrationConnectionString = migrationConnectionString;
        Name = name;
        ConnectionString = new NpgsqlConnectionStringBuilder(migrationConnectionString)
        {
            SearchPath = $"{name}, public"
        }.ConnectionString;
        RuntimeConnectionString = new NpgsqlConnectionStringBuilder(migrationConnectionString)
        {
            SearchPath = $"{name}, public",
            Username = PostgresTestSupport.RuntimeRoleName,
            Password = PostgresTestSupport.RuntimeRolePassword
        }.ConnectionString;
    }

    public string Name { get; }
    public string ConnectionString { get; }
    public string RuntimeConnectionString { get; }

    public static async Task<PostgresTemporarySchema> CreateAsync(string migrationConnectionString)
    {
        var name = $"regent_test_{Guid.NewGuid():N}";
        await PostgresTestSupport.EnsureRuntimeRoleAsync(migrationConnectionString);
        await using var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA \"{name}\";";
        await command.ExecuteNonQueryAsync();
        return new PostgresTemporarySchema(migrationConnectionString, name);
    }

    public Task ApplyMigrationAsync(int version, CancellationToken cancellationToken = default) =>
        ApplyMigrationAsync(version, ConnectionString, cancellationToken);

    public Task ApplyMigrationAsRuntimeRoleAsync(int version, CancellationToken cancellationToken = default) =>
        ApplyMigrationAsync(version, RuntimeConnectionString, cancellationToken);

    public Task ApplyMigrationAsRuntimeRoleAsync(int version, Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ApplyMigrationAsync(version, RuntimeConnectionString, cancellationToken,
            $"SET app.tenant_id = '{tenantId:D}';");

    private async Task ApplyMigrationAsync(int version, string connectionString,
        CancellationToken cancellationToken, string? sessionSetup = null)
    {
        var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql", $"controlplane-{version:000}.sql");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var manifestChecksum = PostgresMigrationChecksumManifest.GetChecksum(version);
        var fileChecksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(fileChecksum, manifestChecksum, StringComparison.Ordinal))
            throw new InvalidOperationException($"Migration {Path.GetFileName(path)} does not match the embedded checksum manifest.");
        var sql = Encoding.UTF8.GetString(bytes);
        if (!string.IsNullOrWhiteSpace(sessionSetup)) sql = $"{sessionSetup}\n{sql}";
        await ExecuteAsync(sql, connectionString, cancellationToken);
        var hasChecksum = Equals(true, await ScalarAsync("""
            SELECT EXISTS (
                SELECT 1 FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
                  AND a.attname = 'Checksum' AND NOT a.attisdropped
            );
            """, cancellationToken));
        if (hasChecksum) await SetMigrationChecksumAsync(version, manifestChecksum, cancellationToken);
        if (version == 8) await BackfillMigrationChecksumsAsync(cancellationToken);
    }

    public async Task MakeRuntimeRoleTableOwnerAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync($"""
            GRANT USAGE, CREATE ON SCHEMA "{Name}" TO "{PostgresTestSupport.RuntimeRoleName}";
            DO $ownership$
            DECLARE
                table_name text;
            BEGIN
                FOR table_name IN SELECT tablename FROM pg_tables WHERE schemaname = current_schema() LOOP
                    EXECUTE format('ALTER TABLE %I OWNER TO "{PostgresTestSupport.RuntimeRoleName}"', table_name);
                END LOOP;
            END
            $ownership$;
            """, cancellationToken);
    }

    public async Task ApplyRuntimeGrantsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql", "runtime-grants.sql");
        await ExecuteAsync(await File.ReadAllTextAsync(path, cancellationToken), cancellationToken);
    }

    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default) =>
        ExecuteAsync(sql, ConnectionString, cancellationToken);

    private async Task ExecuteAsync(string sql, string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value switch
        {
            int number => (long)number,
            short number => (long)number,
            DBNull => null,
            _ => value
        };
    }

    public async Task<IReadOnlyList<SchemaCommentCoverage>> VerifySchemaCommentsAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql", "verify-schema-comments.sql");
        var sql = await File.ReadAllTextAsync(path, cancellationToken);
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var coverage = new List<SchemaCommentCoverage>();
        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.FieldCount == 6 && reader.GetName(0) == "object_type")
                {
                    var objectTypeOrdinal = reader.GetOrdinal("object_type");
                    var totalOrdinal = reader.GetOrdinal("total");
                    var commentedOrdinal = reader.GetOrdinal("commented");
                    var missingOrdinal = reader.GetOrdinal("missing");
                    var coveragePercentOrdinal = reader.GetOrdinal("coverage_percent");
                    var missingObjectsOrdinal = reader.GetOrdinal("missing_objects");
                    coverage.Add(new SchemaCommentCoverage(
                        reader.GetString(objectTypeOrdinal),
                        reader.GetInt64(totalOrdinal),
                        reader.GetInt64(commentedOrdinal),
                        reader.GetInt64(missingOrdinal),
                        reader.GetDecimal(coveragePercentOrdinal),
                        reader.GetString(missingObjectsOrdinal)));
                }
            }
        } while (await reader.NextResultAsync(cancellationToken));
        return coverage;
    }

    public async Task<string> BusinessRowCountsAsync(CancellationToken cancellationToken = default)
    {
        return (string)(await ScalarAsync("""
            SELECT coalesce(jsonb_object_agg("relname", "row_count")::text, '{}')
            FROM (
                SELECT c.relname,
                       (xpath('/row/n/text()', query_to_xml(format('SELECT count(*) AS n FROM %I.%I', n.nspname, c.relname),
                            false, true, '')))[1]::text::bigint AS row_count
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                  AND c.relname <> 'SchemaVersions'
            ) counts;
            """, cancellationToken))!;
    }

    public async Task<string> CatalogSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return (string)(await ScalarAsync("""
            SELECT coalesce(jsonb_agg(to_jsonb(snapshot) ORDER BY snapshot.kind, snapshot.identity)::text, '[]')
            FROM (
                SELECT 'table' AS kind, format('%I.%I', n.nspname, c.relname) AS identity,
                       jsonb_build_object('relkind', c.relkind, 'rls', c.relrowsecurity, 'force_rls', c.relforcerowsecurity,
                                          'owner', pg_get_userbyid(c.relowner), 'persistence', c.relpersistence) AS definition
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                UNION ALL
                SELECT 'column', format('%I.%I.%I', n.nspname, c.relname, a.attname),
                       jsonb_build_object('type', format_type(a.atttypid, a.atttypmod), 'not_null', a.attnotnull,
                                          'default', pg_get_expr(ad.adbin, ad.adrelid), 'identity', a.attidentity,
                                          'generated', a.attgenerated)
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                LEFT JOIN pg_attrdef ad ON ad.adrelid = c.oid AND ad.adnum = a.attnum
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                  AND a.attnum > 0 AND NOT a.attisdropped
                UNION ALL
                SELECT 'constraint', format('%I.%I.%I', n.nspname, c.relname, con.conname),
                       jsonb_build_object('type', con.contype, 'definition', pg_get_constraintdef(con.oid),
                                          'deferrable', con.condeferrable, 'deferred', con.condeferred,
                                          'validated', con.convalidated)
                FROM pg_constraint con
                JOIN pg_class c ON c.oid = con.conrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                UNION ALL
                SELECT 'index', format('%I.%I.%I', n.nspname, c.relname, ic.relname),
                       jsonb_build_object('definition', pg_get_indexdef(i.indexrelid), 'unique', i.indisunique,
                                          'valid', i.indisvalid, 'method', am.amname)
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_class ic ON ic.oid = i.indexrelid
                JOIN pg_am am ON am.oid = ic.relam
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                UNION ALL
                SELECT 'policy', format('%I.%I.%I', n.nspname, c.relname, p.polname),
                       jsonb_build_object('command', p.polcmd, 'permissive', p.polpermissive,
                                          'roles', p.polroles::text, 'using', pg_get_expr(p.polqual, p.polrelid),
                                          'check', pg_get_expr(p.polwithcheck, p.polrelid))
                FROM pg_policy p
                JOIN pg_class c ON c.oid = p.polrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                UNION ALL
                SELECT 'privilege', format('%I.%I.%I.%I.%s', n.nspname, c.relname,
                                           coalesce(role.rolname, 'PUBLIC'), acl.privilege_type, acl.is_grantable),
                       jsonb_build_object('grantee', acl.grantee, 'grantable', acl.is_grantable)
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                CROSS JOIN LATERAL aclexplode(CASE WHEN c.relacl IS NULL THEN acldefault('r', c.relowner)
                                                   ELSE c.relacl END) acl
                LEFT JOIN pg_roles role ON role.oid = acl.grantee
                WHERE n.nspname = current_schema() AND c.relkind IN ('r', 'p')
                UNION ALL
                SELECT 'extension', e.extname,
                       jsonb_build_object('version', e.extversion, 'schema', e.extnamespace::regnamespace::text)
                FROM pg_extension e
                WHERE e.extname IN ('vector', 'ltree')
            ) snapshot;
            """, cancellationToken))!;
    }

    public async Task InsertTenantAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "Tenants" ("Id", "Name", "CompanyName", "Status")
            VALUES (@id, 'Synthetic', 'Synthetic', 'Active');
            """;
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertApprovalBefore007Async(Guid tenantId, Guid approvalId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Approvals" ("Id", "TenantId", "ActionType", "Title", "Impact", "Severity", "State",
                "Requester", "CreatedAt", "ExpiresAt", "OptionsJson", "EvidenceJson")
            VALUES (@approval_id, @tenant_id, 'employee.terminate', 'Legacy approval', 'Legacy', 'High', 'Approved',
                'Legacy requester', now(), now() + interval '1 hour', '["Approve"]', '[]');
            """;
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("approval_id", approvalId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertProjectAsync(Guid tenantId, Guid id, string name,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Projects" ("Id", "TenantId", "Name", "Mission", "Owner", "Stage", "Health",
                "TargetAt", "BudgetMinor", "SpentMinor", "Currency", "UpdatedAt")
            VALUES (@id, @tenant_id, @name, 'Synthetic mission', 'Synthetic owner', 'Active', 'Healthy',
                now(), 100, 0, 'CNY', now());
            """;
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("name", name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "Users" ("Id", "Email", "DisplayName")
            VALUES (@id, @email, 'Synthetic user');
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", $"{id:N}@example.test");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertMembershipAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Memberships" ("Id", "TenantId", "UserId", "Role", "CapabilitiesJson")
            VALUES (@id, @tenant_id, @user_id, 'Owner', '[]');
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertMilestoneBefore009Async(Guid projectId, Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "Milestones" ("Id", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
            VALUES (@id, @project_id, 'Before schema 9', now(), 'Pending', 'Synthetic acceptance');
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("project_id", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertMilestoneAsync(Guid tenantId, Guid projectId, Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Milestones" ("Id", "TenantId", "ProjectId", "Name", "TargetAt", "Status", "AcceptanceRule")
            VALUES (@id, @tenant_id, @project_id, 'Synthetic milestone', now(), 'Pending', 'Synthetic acceptance');
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("project_id", projectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InsertOutboxAsync(Guid tenantId, Guid id, string eventType,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Outbox" ("Id", "TenantId", "EventType", "PayloadJson", "Status", "Attempts", "CreatedAt")
            VALUES (@id, @tenant_id, @event_type, '{}', 'Pending', 0, now());
            """;
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("event_type", eventType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task InsertPositionBefore005Async(Guid tenantId, Guid id, Guid? parentId,
        CancellationToken cancellationToken = default) =>
        InsertPositionCoreAsync(tenantId, id, parentId, null, cancellationToken);

    public Task InsertPositionAsync(Guid tenantId, Guid id, Guid? parentId, string path,
        CancellationToken cancellationToken = default)
    {
        PositionPath.Validate(path, id);
        return InsertPositionCoreAsync(tenantId, id, parentId, path, cancellationToken);
    }

    public async Task SetSchemaCommentAsync(string objectType, string? comment,
        CancellationToken cancellationToken = default)
    {
        var target = objectType switch
        {
            "table" => "TABLE \"Tenants\"",
            "column" => "COLUMN \"Tenants\".\"Name\"",
            "index" => "INDEX \"PK_Tenants\"",
            "constraint" => "CONSTRAINT \"PK_Tenants\" ON \"Tenants\"",
            "extension" => "EXTENSION \"vector\"",
            _ => throw new ArgumentOutOfRangeException(nameof(objectType))
        };
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var literal = comment is null ? "NULL" : $"'{comment.Replace("'", "''")}'";
        command.CommandText = $"COMMENT ON {target} IS {literal};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SetMigrationChecksumAsync(int version, string checksum,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE \"SchemaVersions\" SET \"Checksum\" = @checksum WHERE \"Version\" = @version;";
        command.Parameters.AddWithValue("checksum", checksum);
        command.Parameters.AddWithValue("version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task BackfillMigrationChecksumsAsync(CancellationToken cancellationToken)
    {
        foreach (var (version, checksum) in PostgresMigrationChecksumManifest.Required.OrderBy(pair => pair.Key))
            await SetMigrationChecksumAsync(version, checksum, cancellationToken);
    }

    private async Task InsertPositionCoreAsync(Guid tenantId, Guid id, Guid? parentId, string? path,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var pathColumn = path is null ? string.Empty : "\"Path\", ";
        var pathValue = path is null ? string.Empty : "@path::ltree, ";
        command.CommandText = $"""
            SELECT set_config('app.tenant_id', @tenant_id::text, true);
            INSERT INTO "Positions" ("Id", "TenantId", "Name", "Responsibilities", "CapabilitiesJson", "BudgetMinor", "ParentPositionId", {pathColumn}"Status", "Version")
            VALUES (@id, @tenant_id, 'Synthetic', 'Synthetic', '[]', 0, @parent_id, {pathValue}'Open', 1);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("parent_id", (object?)parentId ?? DBNull.Value);
        if (path is not null) command.Parameters.AddWithValue("path", path);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{Name}\" CASCADE;";
        await command.ExecuteNonQueryAsync();
    }
}
