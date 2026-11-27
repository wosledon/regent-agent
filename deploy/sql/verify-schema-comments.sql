-- Verify nonblank Chinese catalog comments without changing persistent schema objects.
-- Run with the same search_path/current schema as the migrated ControlPlane database.
BEGIN;

CREATE TEMP TABLE "_regent_schema_comment_coverage" (
    "object_type" text PRIMARY KEY,
    "total" bigint NOT NULL,
    "commented" bigint NOT NULL,
    "missing_objects" text NOT NULL
) ON COMMIT DROP;

INSERT INTO "_regent_schema_comment_coverage" ("object_type", "total", "commented", "missing_objects")
WITH expected_object_types(object_type) AS (
    VALUES ('tables'), ('columns'), ('constraints'), ('indexes'), ('extensions')
), table_objects AS (
    SELECT c.oid, n.nspname, c.relname,
           obj_description(c.oid, 'pg_class') AS description
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = current_schema()
      AND c.relkind IN ('r', 'p')
      AND c.relpersistence <> 't'
      AND NOT EXISTS (
          SELECT 1 FROM pg_depend d
          WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e'
      )
), all_objects AS (
    SELECT 'tables' AS object_type,
           format('%I.%I', nspname, relname) AS object_name,
           description
    FROM table_objects
    UNION ALL
    SELECT 'columns',
           format('%I.%I.%I', t.nspname, t.relname, a.attname),
           col_description(t.oid, a.attnum)
    FROM table_objects t
    JOIN pg_attribute a ON a.attrelid = t.oid
    WHERE a.attnum > 0 AND NOT a.attisdropped
      AND NOT (t.relname = 'SchemaVersions' AND a.attname = 'Checksum')
    UNION ALL
    SELECT 'constraints',
           format('%I.%I.%I', t.nspname, t.relname, con.conname),
           obj_description(con.oid, 'pg_constraint')
    FROM table_objects t
    -- PG18 exposes table NOT NULL attributes as contype='n'; only p/f/u/c are relational constraints.
    JOIN pg_constraint con ON con.conrelid = t.oid
      AND con.contype IN ('p', 'f', 'u', 'c')
    UNION ALL
    SELECT 'indexes',
           format('%I.%I', n.nspname, ic.relname),
           obj_description(ic.oid, 'pg_class')
    FROM pg_index i
    JOIN table_objects t ON t.oid = i.indrelid
    JOIN pg_class ic ON ic.oid = i.indexrelid
    JOIN pg_namespace n ON n.oid = ic.relnamespace
    WHERE n.nspname = current_schema()
      AND ic.relkind IN ('i', 'I')
      AND NOT EXISTS (
          SELECT 1 FROM pg_depend d
          WHERE d.classid = 'pg_class'::regclass AND d.objid = ic.oid AND d.deptype = 'e'
      )
    UNION ALL
    SELECT 'extensions',
           required.extname,
           obj_description(e.oid, 'pg_extension')
    FROM (VALUES ('vector'), ('ltree')) AS required(extname)
    LEFT JOIN pg_extension e ON e.extname = required.extname
), coverage AS (
    SELECT object_type, count(*) AS total,
           count(*) FILTER (WHERE nullif(btrim(description), '') ~ '[一-鿿]') AS commented,
           coalesce(string_agg(object_name, E'\n' ORDER BY object_name)
                    FILTER (WHERE (nullif(btrim(description), '') ~ '[一-鿿]') IS NOT TRUE), '') AS missing_objects
    FROM all_objects
    GROUP BY object_type
)
SELECT expected.object_type,
       coalesce(coverage.total, 0) AS total,
       coalesce(coverage.commented, 0) AS commented,
       coalesce(coverage.missing_objects, '') AS missing_objects
FROM expected_object_types expected
LEFT JOIN coverage ON coverage.object_type = expected.object_type;

DO $verify$
DECLARE
    item record;
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
          AND a.attname = 'Checksum' AND a.attnum > 0 AND NOT a.attisdropped
          AND (nullif(btrim(col_description(c.oid, a.attnum)), '') ~ '[一-鿿]') IS NOT TRUE
    ) THEN
        RAISE EXCEPTION 'SchemaVersions.Checksum must have a nonblank Chinese comment in schema %', current_schema();
    END IF;

    FOR item IN SELECT * FROM "_regent_schema_comment_coverage" ORDER BY "object_type" LOOP
        IF item."total" = 0 THEN
            RAISE EXCEPTION 'Schema comment verification has a zero-object denominator for % in schema %',
                item."object_type", current_schema();
        END IF;
        IF item."commented" <> item."total" THEN
            RAISE EXCEPTION 'Schema comment verification failed for % in schema %: %/% commented; missing, blank, or non-Chinese:%',
                item."object_type", current_schema(), item."commented", item."total",
                E'\n' || item."missing_objects";
        END IF;
        RAISE NOTICE 'schema=% type=% commented=%/% coverage=100%%',
            current_schema(), item."object_type", item."commented", item."total";
    END LOOP;
END
$verify$;

SELECT "object_type", "total", "commented", "total" - "commented" AS "missing",
       CASE WHEN "total" = 0 THEN 0 ELSE round(100.0 * "commented" / "total", 2) END AS "coverage_percent",
       "missing_objects"
FROM "_regent_schema_comment_coverage"
ORDER BY "object_type";
COMMIT;
