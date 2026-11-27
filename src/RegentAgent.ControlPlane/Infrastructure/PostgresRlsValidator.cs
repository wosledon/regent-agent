using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public static class PostgresRlsValidator
{
    public static async Task ValidateAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return;
        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("TenantId") is not null)
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Append("MemoryEmbeddings")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                WITH expected(table_name) AS (
                    SELECT unnest(@tables)
                ), physical_tenant_tables AS (
                    SELECT c.oid, c.relname
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema()
                      AND c.relkind IN ('r', 'p')
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_depend d
                          WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e'
                      )
                      AND EXISTS (
                          SELECT 1 FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'TenantId'
                            AND a.attnum > 0 AND NOT a.attisdropped
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
                ), issues AS (
                    SELECT format('tenant table %s is missing', e.table_name) AS issue
                    FROM expected e
                    WHERE to_regclass(format('%I.%I', current_schema(), e.table_name)) IS NULL
                    UNION ALL
                    SELECT format('unmapped tenant table %s must be added to the RLS allowlist', t.relname)
                    FROM physical_tenant_tables t
                    WHERE NOT EXISTS (SELECT 1 FROM expected e WHERE e.table_name = t.relname)
                    UNION ALL
                    SELECT 'runtime role is or inherits superuser'
                    FROM pg_roles r
                    WHERE (r.rolname = current_user OR pg_has_role(current_user, r.oid, 'MEMBER')) AND r.rolsuper
                    UNION ALL
                    SELECT 'runtime role is or inherits BYPASSRLS'
                    FROM pg_roles r
                    WHERE (r.rolname = current_user OR pg_has_role(current_user, r.oid, 'MEMBER')) AND r.rolbypassrls
                    UNION ALL
                    SELECT 'runtime role is or inherits CREATEROLE'
                    FROM pg_roles r
                    WHERE (r.rolname = current_user OR pg_has_role(current_user, r.oid, 'MEMBER')) AND r.rolcreaterole
                    UNION ALL
                    SELECT 'runtime role is or inherits CREATEDB'
                    FROM pg_roles r
                    WHERE (r.rolname = current_user OR pg_has_role(current_user, r.oid, 'MEMBER')) AND r.rolcreatedb
                    UNION ALL
                    SELECT 'runtime role owns or can assume ownership of the application schema'
                    FROM pg_namespace n
                    WHERE n.nspname = current_schema()
                      AND (pg_get_userbyid(n.nspowner) = current_user OR pg_has_role(current_user, n.nspowner, 'MEMBER'))
                    UNION ALL
                    SELECT 'runtime role has effective CREATE on the application schema'
                    WHERE has_schema_privilege(current_user, current_schema(), 'CREATE')
                    UNION ALL
                    SELECT format('runtime role owns application table %s', t.relname)
                    FROM application_tables t
                    WHERE pg_get_userbyid(t.relowner) = current_user
                    UNION ALL
                    SELECT format('runtime role can assume owner role for application table %s', t.relname)
                    FROM application_tables t
                    WHERE t.relowner <> (SELECT oid FROM pg_roles WHERE rolname = current_user)
                      AND pg_has_role(current_user, t.relowner, 'MEMBER')
                    UNION ALL
                    SELECT format('tenant table %s must enable and force RLS', c.relname)
                    FROM expected e
                    JOIN pg_class c ON c.oid = to_regclass(format('%I.%I', current_schema(), e.table_name))
                    WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                    UNION ALL
                    SELECT format('tenant table %s is missing the exact canonical permissive ALL policy', c.relname)
                    FROM expected e
                    JOIN pg_class c ON c.oid = to_regclass(format('%I.%I', current_schema(), e.table_name))
                    WHERE NOT EXISTS (
                        SELECT 1 FROM canonical_policies p WHERE p.polrelid = c.oid
                    )
                    UNION ALL
                    SELECT format('tenant table %s has unexpected permissive policy %s', c.relname, p.polname)
                    FROM expected e
                    JOIN pg_class c ON c.oid = to_regclass(format('%I.%I', current_schema(), e.table_name))
                    JOIN pg_policy p ON p.polrelid = c.oid
                    WHERE p.polpermissive
                      AND NOT EXISTS (
                          SELECT 1 FROM valid_policies v
                          WHERE v.polrelid = p.polrelid AND v.polname = p.polname
                      )
                    UNION ALL
                    SELECT 'Memberships must contain exactly one exact Memberships_user_read SELECT policy'
                    WHERE (
                        SELECT count(*)
                        FROM pg_policy p
                        JOIN pg_class c ON c.oid = p.polrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema()
                          AND {PostgresRlsPolicyCatalog.MembershipUserReadPolicy("p", "c")}
                    ) <> 1
                    UNION ALL
                    SELECT format('runtime role has unsafe effective privileges on append-only table %s', t.relname)
                    FROM application_tables t
                    WHERE t.relname IN ('AuditRecords', 'AuditCheckpoints')
                      AND (
                          NOT has_table_privilege(current_user, t.oid, 'SELECT')
                          OR NOT has_table_privilege(current_user, t.oid, 'INSERT')
                          OR has_table_privilege(current_user, t.oid, 'UPDATE')
                          OR has_table_privilege(current_user, t.oid, 'DELETE')
                          OR has_table_privilege(current_user, t.oid, 'TRUNCATE')
                          OR has_table_privilege(current_user, t.oid, 'REFERENCES')
                          OR has_table_privilege(current_user, t.oid, 'TRIGGER')
                      )
                    UNION ALL
                    SELECT 'runtime role has unsafe effective privileges on SchemaVersions'
                    FROM application_tables t
                    WHERE t.relname = 'SchemaVersions'
                      AND (
                          NOT has_table_privilege(current_user, t.oid, 'SELECT')
                          OR has_table_privilege(current_user, t.oid, 'INSERT')
                          OR has_table_privilege(current_user, t.oid, 'UPDATE')
                          OR has_table_privilege(current_user, t.oid, 'DELETE')
                          OR has_table_privilege(current_user, t.oid, 'TRUNCATE')
                          OR has_table_privilege(current_user, t.oid, 'REFERENCES')
                          OR has_table_privilege(current_user, t.oid, 'TRIGGER')
                      )
                    UNION ALL
                    SELECT format('runtime role has unsafe column privilege %s on %s.%s',
                        privilege.name, t.relname, a.attname)
                    FROM application_tables t
                    JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum > 0 AND NOT a.attisdropped
                    CROSS JOIN LATERAL (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('REFERENCES')) AS privilege(name)
                    WHERE t.relname IN ('AuditRecords', 'AuditCheckpoints', 'SchemaVersions')
                      AND has_column_privilege(current_user, t.oid, a.attname, privilege.name)
                      AND NOT (
                          (t.relname IN ('AuditRecords', 'AuditCheckpoints') AND privilege.name IN ('SELECT', 'INSERT'))
                          OR (t.relname = 'SchemaVersions' AND privilege.name = 'SELECT')
                      )
                )
                SELECT coalesce(string_agg(issue, E'\n' ORDER BY issue), '') FROM issues;
                """;
            var parameter = (NpgsqlParameter)command.CreateParameter();
            parameter.ParameterName = "tables";
            parameter.Value = tables;
            parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
            command.Parameters.Add(parameter);
            var issues = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(issues))
                throw new InvalidOperationException($"Postgres RLS validation failed:{Environment.NewLine}{issues}");
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
