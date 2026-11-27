#!/usr/bin/env python3
"""Apply versioned ControlPlane SQL with rolling-upgrade-compatible advisory locks."""
import argparse
import hashlib
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile

from migration_checksum_manifest import load_manifest, validate_files_against_manifest

ROOT = Path(__file__).resolve().parents[1]
SQL_DIR = ROOT / "deploy" / "sql"
CHECKSUM_COLUMN_VERSION = 8
MIGRATION_LOCK_KEY = 6_317_000
LOCK_TIMEOUT = "5s"
STATEMENT_TIMEOUT = "15min"


def migrations(sql_dir, phase="expand"):
    files = sorted(Path(sql_dir).glob("controlplane-[0-9][0-9][0-9]*.sql"))
    if phase == "contract":
        files = [path for path in files if path.stem.endswith("-contract")]
    elif phase == "expand":
        files = [path for path in files if not path.stem.endswith("-contract")]
    if not files:
        raise SystemExit(f"No ControlPlane {phase} migrations found.")
    return files


def version(path):
    match = re.search(r"controlplane-(\d+)(?:-contract)?\.sql$", path.name)
    if not match:
        raise SystemExit(f"Invalid migration name: {path.name}")
    return int(match.group(1))


def checksum(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_files(files):
    for path in files:
        sql = path.read_bytes()
        digest = hashlib.sha256(sql).hexdigest()
        rls = len(re.findall(rb"FORCE\s+ROW\s+LEVEL\s+SECURITY", sql, re.IGNORECASE))
        print(f"{path.name} version={version(path)} sha256={digest} forced_rls_declarations={rls}")
        if b"BEGIN;" not in sql or b"COMMIT;" not in sql:
            raise SystemExit(f"Migration {path.name} must be transactional.")
    versions = [version(path) for path in files]
    if len(versions) != len(set(versions)):
        raise SystemExit("Migration versions must be unique.")


def validated_checksums(files, manifest_path):
    try:
        manifest = load_manifest(manifest_path)
        expected = validate_files_against_manifest(files, manifest)
        return {version: checksum for version, (_, checksum) in expected.items()}
    except ValueError as error:
        raise SystemExit(str(error)) from error


def migration_lock_keys(files):
    return [MIGRATION_LOCK_KEY, *(MIGRATION_LOCK_KEY + version(path) for path in files)]


def lock_statements(files):
    return [f"SELECT pg_advisory_lock({key});" for key in migration_lock_keys(files)]


def unlock_statements(files):
    return [f"SELECT pg_advisory_unlock({key});" for key in reversed(migration_lock_keys(files))]


def _split_npgsql_keywords(value):
    parts = []
    current = []
    quote = None
    escaped = False
    index = 0
    while index < len(value):
        character = value[index]
        if escaped:
            current.append(character)
            escaped = False
        elif character == "\\":
            current.append(character)
            escaped = True
        elif quote is not None:
            if character == quote:
                if index + 1 < len(value) and value[index + 1] == quote:
                    current.append(quote)
                    index += 1
                else:
                    quote = None
            else:
                current.append(character)
        elif character in ("'", '"'):
            quote = character
        elif character == ";":
            if "".join(current).strip():
                parts.append("".join(current).strip())
            current = []
        else:
            current.append(character)
        index += 1
    if quote is not None:
        raise SystemExit("Unterminated quote in PostgreSQL connection string.")
    if "".join(current).strip():
        parts.append("".join(current).strip())
    return parts


def _libpq_value(value):
    return "'" + value.replace("\\", "\\\\").replace("'", "\\'") + "'"


def libpq_dsn(database_url):
    """Convert the Npgsql keyword form accepted by .NET configuration to libpq form."""
    value = database_url.strip()
    if value.startswith(("postgresql://", "postgres://")):
        return value
    if not value:
        raise SystemExit("PostgreSQL connection string is empty.")

    keyword_map = {
        "host": "host", "server": "host",
        "port": "port",
        "database": "dbname", "dbname": "dbname",
        "username": "user", "userid": "user", "uid": "user", "user": "user",
        "password": "password", "pwd": "password",
        "timeout": "connect_timeout", "connecttimeout": "connect_timeout",
        "sslmode": "sslmode", "sslrootcert": "sslrootcert", "sslcert": "sslcert",
        "sslkey": "sslkey", "applicationname": "application_name", "options": "options",
    }
    if ";" in value:
        parts = _split_npgsql_keywords(value)
    else:
        lexer = shlex.shlex(value, posix=True)
        lexer.whitespace_split = True
        lexer.commenters = ""
        parts = list(lexer)
    converted = []
    options = []
    for part in parts:
        if "=" not in part:
            raise SystemExit(f"Invalid PostgreSQL connection keyword: {part!r}.")
        key, item = part.split("=", 1)
        normalized = re.sub(r"[\s_-]", "", key).lower()
        if normalized == "searchpath":
            if item:
                options.append(f"-csearch_path={item}")
            continue
        target = keyword_map.get(normalized)
        if target is None:
            raise SystemExit(f"Unsupported Npgsql connection keyword for psql: {key}.")
        if target == "options":
            if item:
                options.append(item)
        else:
            converted.append((target, item))
    if options:
        converted.append(("options", " ".join(options)))
    if not converted:
        raise SystemExit("PostgreSQL connection string contains no libpq connection keywords.")
    return " ".join(f"{key}={_libpq_value(item)}" for key, item in converted)


def run_psql(database_url, arguments, echo=True):
    result = subprocess.run(
        ["psql", "-X", "-v", "ON_ERROR_STOP=1", libpq_dsn(database_url), *arguments],
        capture_output=True,
        text=True,
    )
    if echo:
        if result.stdout:
            print(result.stdout, end="")
        if result.stderr:
            print(result.stderr, end="", file=sys.stderr)
    if result.returncode != 0:
        if not echo:
            if result.stdout:
                print(result.stdout, end="", file=sys.stderr)
            if result.stderr:
                print(result.stderr, end="", file=sys.stderr)
        raise SystemExit(f"psql exited with status {result.returncode}.")
    return result


def _expected_values(files, checksums=None):
    values = []
    for path in files:
        target = version(path)
        digest = checksums[target] if checksums is not None else checksum(path)
        values.append(f"({target}, '{digest}')")
    return ", ".join(values)


def _version_values(files):
    return ", ".join(f"({version(path)})" for path in files)


def history_validation_sql(files, strict=False, checksums=None):
    expected = _expected_values(files, checksums)
    versions = _version_values(files)
    newest = max(version(path) for path in files)
    if strict:
        return f"""
DO $regent_migration_validation$
DECLARE
    latest integer;
    has_checksum boolean;
    checksum_drift boolean := false;
BEGIN
    IF to_regclass('"SchemaVersions"') IS NULL THEN
        RAISE EXCEPTION 'Database schema history is missing.';
    END IF;
    SELECT COALESCE(MAX("Version"), 0) INTO latest FROM "SchemaVersions";
    IF latest <> {newest} THEN
        RAISE EXCEPTION 'Newest migration % did not record schema version %.', latest, {newest};
    END IF;
    IF EXISTS (
        SELECT expected.version FROM (VALUES {versions}) AS expected(version)
        EXCEPT SELECT history."Version" FROM "SchemaVersions" history
    ) OR EXISTS (
        SELECT history."Version" FROM "SchemaVersions" history
        EXCEPT SELECT expected.version FROM (VALUES {versions}) AS expected(version)
    ) THEN
        RAISE EXCEPTION 'Database schema history must exactly match migration versions {versions}.';
    END IF;
    SELECT EXISTS (
        SELECT 1 FROM pg_attribute a
        JOIN pg_class c ON c.oid = a.attrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
          AND a.attname = 'Checksum' AND a.attnum > 0 AND NOT a.attisdropped
    ) INTO has_checksum;
    IF {newest} >= {CHECKSUM_COLUMN_VERSION} AND NOT has_checksum THEN
        RAISE EXCEPTION 'SchemaVersions.Checksum is required at schema version %.', {newest};
    END IF;
    IF has_checksum THEN
        EXECUTE $regent_checksum_drift$
            SELECT EXISTS (
                SELECT 1 FROM (VALUES {expected}) AS expected(version, checksum)
                LEFT JOIN "SchemaVersions" history ON history."Version" = expected.version
                WHERE history."Version" IS NULL OR history."Checksum" IS DISTINCT FROM expected.checksum
            )
        $regent_checksum_drift$ INTO checksum_drift;
        IF checksum_drift THEN
            RAISE EXCEPTION 'Migration checksum history is incomplete or drifted.';
        END IF;
    END IF;
END
$regent_migration_validation$;
"""
    return f"""
DO $regent_migration_validation$
DECLARE
    latest integer := 0;
    has_checksum boolean := false;
    checksum_drift boolean := false;
BEGIN
    IF to_regclass('"SchemaVersions"') IS NOT NULL THEN
        SELECT COALESCE(MAX("Version"), 0) INTO latest FROM "SchemaVersions";
        IF EXISTS (
            SELECT 1 FROM "SchemaVersions" history
            WHERE history."Version" NOT IN (SELECT version FROM (VALUES {versions}) AS known(version))
        ) THEN
            RAISE EXCEPTION 'Database contains unknown or newer schema versions.';
        END IF;
        IF EXISTS (
            SELECT 1 FROM (VALUES {versions}) AS known(version)
            WHERE known.version <= latest
              AND NOT EXISTS (SELECT 1 FROM "SchemaVersions" history WHERE history."Version" = known.version)
        ) THEN
            RAISE EXCEPTION 'Database schema history is incomplete below version %.', latest;
        END IF;
        SELECT EXISTS (
            SELECT 1 FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
              AND a.attname = 'Checksum' AND a.attnum > 0 AND NOT a.attisdropped
        ) INTO has_checksum;
        IF has_checksum THEN
            EXECUTE $regent_checksum_drift$
                SELECT EXISTS (
                    SELECT 1 FROM "SchemaVersions" history
                    JOIN (VALUES {expected}) AS expected(version, checksum)
                      ON expected.version = history."Version"
                    WHERE history."Checksum" IS NOT NULL AND history."Checksum" <> expected.checksum
                )
            $regent_checksum_drift$ INTO checksum_drift;
            IF checksum_drift THEN
                RAISE EXCEPTION 'Migration checksum drift detected; refusing to overwrite recorded history.';
            END IF;
        END IF;
        IF latest >= {CHECKSUM_COLUMN_VERSION} AND NOT has_checksum THEN
            RAISE EXCEPTION 'SchemaVersions.Checksum is missing at schema version %.', latest;
        END IF;
    END IF;
END
$regent_migration_validation$;
"""


def checksum_backfill_sql(files, checksums=None):
    return f"""
UPDATE "SchemaVersions" AS history
SET "Checksum" = expected.checksum
FROM (VALUES {_expected_values(files, checksums)}) AS expected(version, checksum)
WHERE history."Version" = expected.version AND history."Checksum" IS NULL;
"""


def temporary_sql(contents):
    handle = tempfile.NamedTemporaryFile("w", suffix=".sql", delete=False, encoding="utf-8")
    try:
        handle.write(contents)
        handle.close()
        return Path(handle.name)
    except Exception:
        handle.close()
        os.unlink(handle.name)
        raise


def build_expand_script(files, checksums=None):
    lines = [
        f"SET lock_timeout = '{LOCK_TIMEOUT}';",
        f"SET statement_timeout = '{STATEMENT_TIMEOUT}';",
        *lock_statements(files),
        history_validation_sql(files, checksums=checksums),
        "SELECT to_regclass('\"SchemaVersions\"') IS NOT NULL AS has_schema_versions \\gset",
        "\\if :has_schema_versions",
        "SELECT COALESCE(MAX(\"Version\"), 0) AS latest FROM \"SchemaVersions\" \\gset",
        "\\else",
        "\\set latest 0",
        "\\endif",
    ]
    for path in files:
        target = version(path)
        escaped_path = str(path.resolve()).replace("'", "''")
        lines.extend([
            f"SELECT :latest < {target} AS apply_{target} \\gset",
            f"\\if :apply_{target}",
            f"\\ir '{escaped_path}'",
            "\\else",
            f"\\echo SKIP: {path.name} already applied",
            "\\endif",
        ])
    lines.extend([
        "SELECT EXISTS (SELECT 1 FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid "
        "JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = current_schema() "
        "AND c.relname = 'SchemaVersions' AND a.attname = 'Checksum' AND a.attnum > 0 "
        "AND NOT a.attisdropped) AS has_checksum \\gset",
        "\\if :has_checksum",
        checksum_backfill_sql(files, checksums),
        "\\echo CHECKSUMS: backfilled NULL SHA-256 values without overwriting recorded history",
        "\\endif",
        history_validation_sql(files, strict=True, checksums=checksums),
        *unlock_statements(files),
        f"\\echo PASS: schema version {max(version(path) for path in files)} is ready",
    ])
    return "\n".join(lines)


def build_contract_script(files):
    lines = [
        f"SET lock_timeout = '{LOCK_TIMEOUT}';",
        f"SET statement_timeout = '{STATEMENT_TIMEOUT}';",
        *lock_statements(files),
    ]
    for path in files:
        escaped_path = str(path.resolve()).replace("'", "''")
        lines.extend([f"\\ir '{escaped_path}'", f"\\echo APPLIED: {path.name}"])
    lines.extend([*unlock_statements(files), "\\echo PASS: contract migrations are ready"])
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--database-url", default=os.environ.get("REGENT_POSTGRES_DSN"))
    parser.add_argument("--sql-dir", type=Path, default=SQL_DIR)
    parser.add_argument("--checksum-manifest", type=Path)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--phase", choices=["expand", "contract"], default="expand")
    args = parser.parse_args()
    files = migrations(args.sql_dir, args.phase)
    validate_files(files)
    checksums = None
    if args.phase == "expand":
        manifest_path = args.checksum_manifest or args.sql_dir / "controlplane-checksums.json"
        checksums = validated_checksums(files, manifest_path)
    if args.check or args.dry_run:
        checks = ["migration files", "transactions"]
        if args.phase == "expand":
            checks.extend(["manifest versions", "SHA-256 checksums"])
        print(
            f"PASS: {', '.join(checks)} validated; RLS declarations counted; "
            "exact RLS/policy validation is performed by PostgreSQL integration/startup checks"
        )
        return
    if not args.database_url:
        raise SystemExit("--database-url or REGENT_POSTGRES_DSN is required.")
    psql = subprocess.run(["psql", "--version"], capture_output=True, text=True)
    if psql.returncode != 0:
        raise SystemExit("psql is required for a real migration.")

    contents = build_contract_script(files) if args.phase == "contract" else build_expand_script(files, checksums)
    script = temporary_sql(contents)
    try:
        run_psql(args.database_url, ["-f", str(script)])
    finally:
        script.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
