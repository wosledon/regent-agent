-- Least-privilege grants for the ControlPlane runtime role.
-- Run after the expand migrations with the migration role and the target search_path.
BEGIN;

DO $runtime_grants$
DECLARE
    runtime_role constant text := 'regent_controlplane';
    target record;
    column_record record;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = runtime_role) THEN
        RAISE EXCEPTION 'Required runtime role % does not exist', runtime_role;
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_namespace n
        WHERE n.nspname = current_schema()
          AND (pg_get_userbyid(n.nspowner) = runtime_role
               OR pg_has_role(runtime_role, n.nspowner, 'MEMBER'))
    ) THEN
        RAISE EXCEPTION 'Runtime role % must not own or inherit the application schema', runtime_role;
    END IF;

    EXECUTE format('REVOKE ALL ON SCHEMA %I FROM %I', current_schema(), runtime_role);
    EXECUTE format('REVOKE CREATE ON SCHEMA %I FROM PUBLIC', current_schema());
    EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', current_schema(), runtime_role);

    FOR target IN
        SELECT c.oid, c.relname, c.relkind
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema()
          AND c.relkind IN ('r', 'p')
          AND NOT EXISTS (
              SELECT 1 FROM pg_depend d
              WHERE d.classid = 'pg_class'::regclass AND d.objid = c.oid AND d.deptype = 'e'
          )
        ORDER BY c.relname
    LOOP
        EXECUTE format('REVOKE ALL ON TABLE %I.%I FROM %I, PUBLIC',
            current_schema(), target.relname, runtime_role);

        FOR column_record IN
            SELECT a.attname
            FROM pg_attribute a
            WHERE a.attrelid = target.oid AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
        LOOP
            EXECUTE format('REVOKE ALL PRIVILEGES (%I) ON TABLE %I.%I FROM %I, PUBLIC',
                column_record.attname, current_schema(), target.relname, runtime_role);
        END LOOP;

        IF target.relname = 'SchemaVersions' THEN
            EXECUTE format('GRANT SELECT ON TABLE %I.%I TO %I',
                current_schema(), target.relname, runtime_role);
        ELSIF target.relname IN ('AuditRecords', 'AuditCheckpoints') THEN
            EXECUTE format('GRANT SELECT, INSERT ON TABLE %I.%I TO %I',
                current_schema(), target.relname, runtime_role);
        ELSE
            EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE %I.%I TO %I',
                current_schema(), target.relname, runtime_role);
        END IF;
    END LOOP;

    FOR target IN
        SELECT c.oid, c.relname
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema()
          AND c.relkind = 'S'
        ORDER BY c.relname
    LOOP
        EXECUTE format('REVOKE ALL ON SEQUENCE %I.%I FROM %I, PUBLIC',
            current_schema(), target.relname, runtime_role);
        EXECUTE format('GRANT USAGE, SELECT ON SEQUENCE %I.%I TO %I',
            current_schema(), target.relname, runtime_role);
    END LOOP;

    IF has_schema_privilege(runtime_role, current_schema(), 'CREATE') THEN
        RAISE EXCEPTION 'Runtime role % retains effective CREATE on schema %', runtime_role, current_schema();
    END IF;
END
$runtime_grants$;

COMMIT;
