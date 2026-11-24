-- Guarded Contract phase for schema version 3.
-- This release is additive and has no destructive contract operation.
-- Contract still requires a successful Smoke phase and an explicit operator setting.
BEGIN;
DO $$
BEGIN
    IF current_setting('regent.allow_contract', true) IS DISTINCT FROM 'on' THEN
        RAISE EXCEPTION 'Contract phase requires regent.allow_contract=on after successful smoke';
    END IF;
    IF (SELECT MAX("Version") FROM "SchemaVersions") < 3 THEN
        RAISE EXCEPTION 'Expand migration must be applied before Contract';
    END IF;
END $$;
DO $$
DECLARE
    forced_tables integer;
BEGIN
    SELECT count(*) INTO forced_tables
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = current_schema() AND c.relrowsecurity AND c.relforcerowsecurity;
    IF forced_tables < 22 THEN
        RAISE EXCEPTION 'Expected at least 22 forced RLS tables before Contract';
    END IF;
END $$;
COMMIT;
