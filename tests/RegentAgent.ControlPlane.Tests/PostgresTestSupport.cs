using Npgsql;
using RegentAgent.ControlPlane.Infrastructure;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

internal static class PostgresTestSupport
{
    public const string RuntimeRoleName = "regent_controlplane";
    public const string RuntimeRolePassword = "regent_test_only";

    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "RegentAgent.slnx"))) return directory.FullName;
            }
            throw new InvalidOperationException("Repository root could not be located.");
        }
    }

    public static async Task AssertSupportedVersionAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SHOW server_version_num;";
        var serverVersion = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Contains(serverVersion / 10000, new[] { 17, 18 });
    }

    public static async Task EnsureRuntimeRoleAsync(string migrationConnectionString)
    {
        await using var connection = new NpgsqlConnection(migrationConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_advisory_lock(6517001);
            DO $role$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'regent_controlplane') THEN
                    CREATE ROLE regent_controlplane LOGIN PASSWORD 'regent_test_only';
                END IF;
            END
            $role$;
            SELECT pg_advisory_unlock(6517001);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public static async Task AssertRuntimeRoleAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH tenant_tables AS (
                SELECT c.oid, c.relname, c.relrowsecurity, c.relforcerowsecurity
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.relkind IN ('r', 'p')
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_depend d
                      WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e'
                  )
                  AND (
                      c.relname = 'MemoryEmbeddings'
                      OR EXISTS (
                          SELECT 1 FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'TenantId'
                            AND a.attnum > 0 AND NOT a.attisdropped
                      )
                  )
            ), application_tables AS (
                SELECT c.oid, c.relname, c.relowner
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema()
                  AND c.relkind IN ('r', 'p')
                  AND NOT EXISTS (
                      SELECT 1 FROM pg_depend d
                      WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e'
                  )
            ), canonical_policies AS (
                SELECT p.polrelid, p.polname
                FROM pg_policy p
                JOIN pg_class c ON c.oid = p.polrelid
                WHERE {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")}
            ), valid_policies AS (
                SELECT polrelid, polname FROM canonical_policies
                UNION
                SELECT p.polrelid, p.polname
                FROM pg_policy p
                JOIN pg_class c ON c.oid = p.polrelid
                WHERE {PostgresRlsPolicyCatalog.MembershipUserReadPolicy("p", "c")}
            )
            SELECT
                NOT COALESCE((SELECT bool_or(rolsuper) FROM pg_roles
                    WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')), false),
                NOT COALESCE((SELECT bool_or(rolbypassrls) FROM pg_roles
                    WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')), false),
                NOT COALESCE((SELECT bool_or(rolcreaterole) FROM pg_roles
                    WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')), false),
                NOT COALESCE((SELECT bool_or(rolcreatedb) FROM pg_roles
                    WHERE rolname = current_user OR pg_has_role(current_user, oid, 'MEMBER')), false),
                NOT EXISTS (
                    SELECT 1 FROM pg_namespace n
                    WHERE n.nspname = current_schema()
                      AND (pg_get_userbyid(n.nspowner) = current_user OR pg_has_role(current_user, n.nspowner, 'MEMBER'))
                ),
                NOT has_schema_privilege(current_user, current_schema(), 'CREATE'),
                NOT EXISTS (SELECT 1 FROM application_tables t WHERE pg_get_userbyid(t.relowner) = current_user),
                NOT EXISTS (
                    SELECT 1 FROM application_tables t
                    WHERE t.relowner <> (SELECT oid FROM pg_roles WHERE rolname = current_user)
                      AND pg_has_role(current_user, t.relowner, 'MEMBER')
                ),
                NOT EXISTS (
                    SELECT 1 FROM tenant_tables t WHERE NOT t.relrowsecurity OR NOT t.relforcerowsecurity
                ),
                NOT EXISTS (
                    SELECT 1 FROM tenant_tables t
                    WHERE NOT EXISTS (SELECT 1 FROM canonical_policies p WHERE p.polrelid = t.oid)
                ),
                NOT EXISTS (
                    SELECT 1 FROM tenant_tables t
                    JOIN pg_policy p ON p.polrelid = t.oid
                    WHERE p.polpermissive
                      AND NOT EXISTS (
                          SELECT 1 FROM valid_policies v
                          WHERE v.polrelid = p.polrelid AND v.polname = p.polname
                      )
                ),
                (
                    SELECT count(*)
                    FROM pg_policy p
                    JOIN pg_class c ON c.oid = p.polrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema()
                      AND {PostgresRlsPolicyCatalog.MembershipUserReadPolicy("p", "c")}
                ) = 1;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0), "Runtime role must not be or inherit superuser.");
        Assert.True(reader.GetBoolean(1), "Runtime role must not have or inherit BYPASSRLS.");
        Assert.True(reader.GetBoolean(2), "Runtime role must not have or inherit CREATEROLE.");
        Assert.True(reader.GetBoolean(3), "Runtime role must not have or inherit CREATEDB.");
        Assert.True(reader.GetBoolean(4), "Runtime role must not own or inherit the application schema.");
        Assert.True(reader.GetBoolean(5), "Runtime role must not have effective schema CREATE.");
        Assert.True(reader.GetBoolean(6), "Runtime role must not own application tables.");
        Assert.True(reader.GetBoolean(7), "Runtime role must not inherit an application-table owner role.");
        Assert.True(reader.GetBoolean(8), "Every tenant table must enable and force RLS.");
        Assert.True(reader.GetBoolean(9), "Every tenant table must have the exact canonical tenant policy.");
        Assert.True(reader.GetBoolean(10), "Unexpected or weakened permissive RLS policy detected.");
        Assert.True(reader.GetBoolean(11), "Memberships must contain exactly one exact Memberships_user_read SELECT policy.");
    }

    public static async Task AssertRuntimePrivilegesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                has_table_privilege(current_user, format('%I.%I', current_schema(), 'Projects'), 'SELECT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Projects'), 'INSERT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Projects'), 'UPDATE')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Projects'), 'DELETE')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Milestones'), 'SELECT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Milestones'), 'INSERT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Milestones'), 'UPDATE')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'Milestones'), 'DELETE'),
                has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'SELECT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'INSERT')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'UPDATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'DELETE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'TRUNCATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'REFERENCES')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditRecords'), 'TRIGGER'),
                has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'SELECT')
                    AND has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'INSERT')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'UPDATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'DELETE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'TRUNCATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'REFERENCES')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'AuditCheckpoints'), 'TRIGGER'),
                has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'SELECT')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'INSERT')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'UPDATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'DELETE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'TRUNCATE')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'REFERENCES')
                    AND NOT has_table_privilege(current_user, format('%I.%I', current_schema(), 'SchemaVersions'), 'TRIGGER'),
                NOT has_schema_privilege(current_user, current_schema(), 'CREATE'),
                NOT EXISTS (
                    SELECT 1
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                    CROSS JOIN LATERAL (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) AS privilege(name)
                    WHERE n.nspname = current_schema()
                      AND c.relname IN ('AuditRecords', 'AuditCheckpoints', 'SchemaVersions')
                      AND has_column_privilege(current_user, c.oid, a.attname, privilege.name)
                      AND NOT (
                          (c.relname IN ('AuditRecords', 'AuditCheckpoints') AND privilege.name IN ('SELECT', 'INSERT'))
                          OR (c.relname = 'SchemaVersions' AND privilege.name = 'SELECT')
                      )
                );
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0), "Runtime role must retain tenant-scoped business-table CRUD.");
        Assert.True(reader.GetBoolean(1), "AuditRecords must be append-only for the runtime role.");
        Assert.True(reader.GetBoolean(2), "AuditCheckpoints must be append-only for the runtime role.");
        Assert.True(reader.GetBoolean(3), "SchemaVersions must be read-only for the runtime role.");
        Assert.True(reader.GetBoolean(4), "Runtime role must not have schema CREATE.");
        Assert.True(reader.GetBoolean(5), "Protected tables must not retain unsafe column privileges.");
    }
}
