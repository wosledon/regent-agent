using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public static class PostgresSchemaValidator
{
    public static async Task ValidateAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return;
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var version = await ReadVersionAsync(db, cancellationToken);
            var snapshot = await ReadSnapshotAsync(db, cancellationToken);
            Validate(version, snapshot);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    internal static void Validate(int version, PostgresSchemaSnapshot schema)
    {
        if (version != SchemaVersionService.CurrentVersion)
            throw new InvalidOperationException($"Postgres schema version {SchemaVersionService.CurrentVersion} is required; run the migration job first.");
        Require(schema.VectorExtension, "Postgres vector extension is missing.");
        Require(schema.LTreeExtension, "Postgres ltree extension is missing.");
        Require(schema.MemoryEmbeddingsTable, "Postgres MemoryEmbeddings table is missing.");
        Require(schema.Vector256NotNull, "MemoryEmbeddings.Vector must be vector(256) NOT NULL.");
        Require(schema.HnswCosineIndex, "MemoryEmbeddings HNSW vector_cosine_ops index is missing.");
        Require(schema.PositionsPathLtreeNotNull, "Positions.Path must be ltree NOT NULL.");
        Require(schema.PositionsPathGistIndex, "Positions.Path GiST index is missing.");
        Require(schema.MemoryRlsEnabledAndForced, "MemoryEmbeddings must enable and force RLS.");
        Require(schema.MemoryTenantPolicy, "MemoryEmbeddings tenant isolation policy is missing or incomplete.");
        Require(schema.PositionsRlsEnabledAndForced, "Positions must enable and force RLS.");
        Require(schema.PositionsTenantPolicy, "Positions tenant isolation policy is missing or incomplete.");
        Require(schema.ApprovalsRlsEnabledAndForced, "Approvals must enable and force RLS.");
        Require(schema.ApprovalsTenantPolicy, "Approvals tenant isolation policy is missing or incomplete.");
        Require(schema.ApprovalAuthorityColumns, "Approvals authority columns must have the exact schema 8 types, nullability, and defaults.");
        Require(schema.OutboxRetryColumns, "Outbox retry columns must have the exact schema 8 types, nullability, and defaults.");
        Require(schema.SchemaVersionsChecksumColumn, "SchemaVersions.Checksum must be text NULL with no default.");
        Require(schema.RequiredChecksumHistory,
            "SchemaVersions must contain the exact embedded-manifest SHA-256 checksums for migrations 003 through 009.");
        Require(schema.MilestoneTenantIdColumn, "Milestones.TenantId must be uuid NOT NULL with no default.");
        Require(schema.MilestoneProjectForeignKey, "Milestones composite tenant/project foreign key is missing or incomplete.");
        Require(schema.ProjectsTenantIdUniqueIndex, "Projects tenant/ID unique index is missing or incomplete.");
        Require(schema.MilestoneTenantProjectIndex, "Milestones tenant/project index is missing or incomplete.");
        Require(schema.MilestoneTenantDerivationTrigger, "Milestones tenant derivation trigger is missing or incomplete.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task<int> ReadVersionAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(\"Version\"), 0) FROM \"SchemaVersions\";";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<PostgresSchemaSnapshot> ReadSnapshotAsync(RegentDbContext db,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            SELECT
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector'),
                EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'ltree'),
                EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND c.relkind = 'r'),
                EXISTS (SELECT 1 FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings'
                          AND a.attname = 'Vector' AND a.attnotnull AND format_type(a.atttypid, a.atttypmod) = 'vector(256)'),
                EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace JOIN pg_class ic ON ic.oid = i.indexrelid
                        JOIN pg_am am ON am.oid = ic.relam
                        WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings'
                          AND am.amname = 'hnsw' AND pg_get_indexdef(i.indexrelid) LIKE '%vector_cosine_ops%'),
                EXISTS (SELECT 1 FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'Positions'
                          AND a.attname = 'Path' AND a.attnotnull AND format_type(a.atttypid, a.atttypmod) = 'ltree'),
                EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace JOIN pg_class ic ON ic.oid = i.indexrelid
                        JOIN pg_am am ON am.oid = ic.relam
                        WHERE n.nspname = current_schema() AND c.relname = 'Positions'
                          AND am.amname = 'gist' AND pg_get_indexdef(i.indexrelid) LIKE '%"Path"%'),
                COALESCE((SELECT c.relrowsecurity AND c.relforcerowsecurity FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings'), false),
                EXISTS (SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings'
                          AND {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")}),
                COALESCE((SELECT c.relrowsecurity AND c.relforcerowsecurity FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname = current_schema() AND c.relname = 'Positions'), false),
                EXISTS (SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'Positions'
                          AND {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")}),
                COALESCE((SELECT c.relrowsecurity AND c.relforcerowsecurity FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                          WHERE n.nspname = current_schema() AND c.relname = 'Approvals'), false),
                EXISTS (SELECT 1 FROM pg_policy p JOIN pg_class c ON c.oid = p.polrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'Approvals'
                          AND {PostgresRlsPolicyCatalog.CanonicalTenantPolicy("p", "c")}),
                (
                    WITH expected(column_name, data_type, not_null, default_expression) AS (
                        VALUES
                            ('RequesterUserId', 'uuid', true, $$'00000000-0000-0000-0000-000000000000'::uuid$$),
                            ('TargetEmployeeId', 'uuid', false, NULL::text),
                            ('TargetCandidateId', 'uuid', false, NULL::text),
                            ('TargetPositionId', 'uuid', false, NULL::text),
                            ('ActionHash', 'text', true, $$''::text$$),
                            ('ConsumedAt', 'timestamp with time zone', false, NULL::text),
                            ('ConsumedBy', 'uuid', false, NULL::text),
                            ('DecidedBy', 'uuid', false, NULL::text),
                            ('DecidedAt', 'timestamp with time zone', false, NULL::text)
                    )
                    SELECT count(*) = 9 AND bool_and(COALESCE(
                        a.attname IS NOT NULL
                        AND format_type(a.atttypid, a.atttypmod) = expected.data_type
                        AND a.attnotnull = expected.not_null
                        AND pg_get_expr(ad.adbin, ad.adrelid) IS NOT DISTINCT FROM expected.default_expression,
                        false))
                    FROM expected
                    LEFT JOIN pg_attribute a
                      ON a.attrelid = to_regclass(format('%I.%I', current_schema(), 'Approvals'))
                     AND a.attname = expected.column_name AND a.attnum > 0 AND NOT a.attisdropped
                    LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                ),
                (
                    WITH expected(column_name, data_type, not_null, default_expression) AS (
                        VALUES
                            ('NextAttemptAt', 'timestamp with time zone', false, NULL::text),
                            ('LastError', 'text', false, NULL::text)
                    )
                    SELECT count(*) = 2 AND bool_and(COALESCE(
                        a.attname IS NOT NULL
                        AND format_type(a.atttypid, a.atttypmod) = expected.data_type
                        AND a.attnotnull = expected.not_null
                        AND pg_get_expr(ad.adbin, ad.adrelid) IS NOT DISTINCT FROM expected.default_expression,
                        false))
                    FROM expected
                    LEFT JOIN pg_attribute a
                      ON a.attrelid = to_regclass(format('%I.%I', current_schema(), 'Outbox'))
                     AND a.attname = expected.column_name AND a.attnum > 0 AND NOT a.attisdropped
                    LEFT JOIN pg_attrdef ad ON ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                ),
                EXISTS (
                    SELECT 1 FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
                      AND a.attname = 'Checksum' AND a.attnum > 0 AND NOT a.attisdropped
                      AND format_type(a.atttypid, a.atttypmod) = 'text' AND NOT a.attnotnull
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_attrdef ad
                          WHERE ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                      )
                ),
                (SELECT count(*) FROM "SchemaVersions") = {PostgresMigrationChecksumManifest.Required.Count}
                AND NOT EXISTS (
                    SELECT 1
                    FROM (VALUES {PostgresMigrationChecksumManifest.ChecksumValuesSql}) AS required(version, checksum)
                    LEFT JOIN "SchemaVersions" history ON history."Version" = required.version
                    WHERE history."Version" IS NULL
                       OR to_jsonb(history)->>'Checksum' IS DISTINCT FROM required.checksum
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_attribute a
                    JOIN pg_class c ON c.oid = a.attrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema() AND c.relname = 'Milestones'
                      AND a.attname = 'TenantId' AND a.attnum > 0 AND NOT a.attisdropped
                      AND format_type(a.atttypid, a.atttypmod) = 'uuid'
                      AND a.attnotnull
                      AND NOT EXISTS (
                          SELECT 1 FROM pg_attrdef ad
                          WHERE ad.adrelid = a.attrelid AND ad.adnum = a.attnum
                      )
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_constraint con
                    JOIN pg_class c ON c.oid = con.conrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_class parent ON parent.oid = con.confrelid
                    JOIN pg_namespace parent_ns ON parent_ns.oid = parent.relnamespace
                    WHERE n.nspname = current_schema() AND c.relname = 'Milestones'
                      AND parent_ns.nspname = current_schema() AND parent.relname = 'Projects'
                      AND con.conname = 'FK_Milestones_Projects_ProjectId'
                      AND con.contype = 'f' AND con.convalidated AND con.confdeltype = 'c'
                      AND con.conkey = ARRAY[
                          (SELECT a.attnum FROM pg_attribute a
                           WHERE a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped),
                          (SELECT a.attnum FROM pg_attribute a
                           WHERE a.attrelid = c.oid AND a.attname = 'ProjectId' AND NOT a.attisdropped)
                      ]::smallint[]
                      AND con.confkey = ARRAY[
                          (SELECT a.attnum FROM pg_attribute a
                           WHERE a.attrelid = parent.oid AND a.attname = 'TenantId' AND NOT a.attisdropped),
                          (SELECT a.attnum FROM pg_attribute a
                           WHERE a.attrelid = parent.oid AND a.attname = 'Id' AND NOT a.attisdropped)
                      ]::smallint[]
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_class index_class ON index_class.oid = i.indexrelid
                    JOIN pg_namespace index_ns ON index_ns.oid = index_class.relnamespace
                    JOIN pg_am am ON am.oid = index_class.relam
                    WHERE n.nspname = current_schema() AND c.relname = 'Projects'
                      AND index_ns.nspname = current_schema()
                      AND index_class.relname = 'UQ_Projects_TenantId_Id'
                      AND i.indisunique AND i.indisvalid
                      AND i.indnatts = 2 AND i.indnkeyatts = 2
                      AND i.indpred IS NULL AND i.indexprs IS NULL
                      AND am.amname = 'btree'
                      AND i.indkey[0] = (
                          SELECT a.attnum FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped
                      )
                      AND i.indkey[1] = (
                          SELECT a.attnum FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'Id' AND NOT a.attisdropped
                      )
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_class index_class ON index_class.oid = i.indexrelid
                    JOIN pg_namespace index_ns ON index_ns.oid = index_class.relnamespace
                    JOIN pg_am am ON am.oid = index_class.relam
                    WHERE n.nspname = current_schema() AND c.relname = 'Milestones'
                      AND index_ns.nspname = current_schema()
                      AND index_class.relname = 'IX_Milestones_TenantId_ProjectId'
                      AND NOT i.indisunique AND i.indisvalid
                      AND i.indnatts = 2 AND i.indnkeyatts = 2
                      AND i.indpred IS NULL AND i.indexprs IS NULL
                      AND am.amname = 'btree'
                      AND i.indkey[0] = (
                          SELECT a.attnum FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'TenantId' AND NOT a.attisdropped
                      )
                      AND i.indkey[1] = (
                          SELECT a.attnum FROM pg_attribute a
                          WHERE a.attrelid = c.oid AND a.attname = 'ProjectId' AND NOT a.attisdropped
                      )
                      AND NOT EXISTS (
                          SELECT 1
                          FROM pg_index old_index
                          JOIN pg_class old_index_class ON old_index_class.oid = old_index.indexrelid
                          WHERE old_index.indrelid = c.oid
                            AND old_index_class.relname = 'IX_Milestones_ProjectId'
                      )
                ),
                EXISTS (
                    SELECT 1
                    FROM pg_trigger t
                    JOIN pg_class c ON c.oid = t.tgrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_proc p ON p.oid = t.tgfoid
                    JOIN pg_namespace function_ns ON function_ns.oid = p.pronamespace
                    JOIN pg_language lang ON lang.oid = p.prolang
                    WHERE n.nspname = current_schema() AND c.relname = 'Milestones'
                      AND t.tgname = 'trg_Milestones_derive_tenant_id'
                      AND NOT t.tgisinternal AND t.tgenabled = 'O'
                      AND pg_get_triggerdef(t.oid) LIKE '%BEFORE INSERT OR UPDATE%FOR EACH ROW%'
                      AND function_ns.nspname = current_schema()
                      AND p.proname = 'Milestones_derive_tenant_id'
                      AND p.prorettype = 'trigger'::regtype
                      AND lang.lanname = 'plpgsql'
                      AND p.prosrc LIKE '%NEW."TenantId"%'
                      AND p.prosrc LIKE '%NEW."ProjectId"%'
                      AND p.prosrc LIKE '%"Projects"%'
                );
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Postgres schema catalog is unavailable.");
        return new PostgresSchemaSnapshot(
            reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4),
            reader.GetBoolean(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetBoolean(8), reader.GetBoolean(9),
            reader.GetBoolean(10), reader.GetBoolean(11), reader.GetBoolean(12), reader.GetBoolean(13),
            reader.GetBoolean(14), reader.GetBoolean(15), reader.GetBoolean(16), reader.GetBoolean(17),
            reader.GetBoolean(18), reader.GetBoolean(19), reader.GetBoolean(20), reader.GetBoolean(21));
    }
}

internal sealed record PostgresSchemaSnapshot(
    bool VectorExtension,
    bool LTreeExtension,
    bool MemoryEmbeddingsTable,
    bool Vector256NotNull,
    bool HnswCosineIndex,
    bool PositionsPathLtreeNotNull,
    bool PositionsPathGistIndex,
    bool MemoryRlsEnabledAndForced,
    bool MemoryTenantPolicy,
    bool PositionsRlsEnabledAndForced,
    bool PositionsTenantPolicy,
    bool ApprovalsRlsEnabledAndForced,
    bool ApprovalsTenantPolicy,
    bool ApprovalAuthorityColumns,
    bool OutboxRetryColumns,
    bool SchemaVersionsChecksumColumn,
    bool RequiredChecksumHistory,
    bool MilestoneTenantIdColumn,
    bool MilestoneProjectForeignKey,
    bool ProjectsTenantIdUniqueIndex,
    bool MilestoneTenantProjectIndex,
    bool MilestoneTenantDerivationTrigger);
