import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location("migrate_postgres", ROOT / "scripts" / "migrate-postgres.py")
migrate_postgres = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(migrate_postgres)


def run_migration_script(*arguments):
    return subprocess.run(
        [sys.executable, str(ROOT / "scripts" / "migrate-postgres.py"), *map(str, arguments)],
        capture_output=True,
        text=True,
        check=False,
    )


class PostgresMigrationScriptTests(unittest.TestCase):
    def test_npgsql_keywords_are_converted_to_libpq_keywords(self):
        converted = migrate_postgres.libpq_dsn(
            "Host=localhost;Port=5432;Database=regent_test;Username=postgres;"
            "Password=regent_test_only;Search Path=regent_test, public"
        )

        self.assertIn("host='localhost'", converted)
        self.assertIn("port='5432'", converted)
        self.assertIn("dbname='regent_test'", converted)
        self.assertIn("user='postgres'", converted)
        self.assertIn("password='regent_test_only'", converted)
        self.assertIn("options='-csearch_path=regent_test, public'", converted)
        self.assertNotIn("Username", converted)
        self.assertNotIn("Database", converted)

    def test_libpq_keyword_string_is_preserved(self):
        converted = migrate_postgres.libpq_dsn(
            "host=localhost port=5432 dbname=regent_test user=postgres password=regent_test_only"
        )

        self.assertIn("host='localhost'", converted)
        self.assertIn("dbname='regent_test'", converted)
        self.assertIn("user='postgres'", converted)
        self.assertIn("password='regent_test_only'", converted)

    def test_libpq_uri_is_preserved(self):
        uri = "postgresql://postgres@localhost:5432/regent_test"
        self.assertEqual(uri, migrate_postgres.libpq_dsn(uri))

    def test_quoted_npgsql_values_can_contain_semicolons_and_quotes(self):
        converted = migrate_postgres.libpq_dsn(
            "Host=localhost;Database=regent_test;Username=test;Password='p@ss;''word'"
        )

        self.assertIn("password='p@ss;\\'word'", converted)

    def test_unknown_npgsql_keyword_is_rejected_instead_of_forwarded(self):
        with self.assertRaises(SystemExit):
            migrate_postgres.libpq_dsn("Host=localhost;Database=regent_test;Enlist=False")

    def test_expand_script_uses_compatible_locks_and_only_backfills_null_checksums(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            files = []
            for number in (3, 4):
                path = root / f"controlplane-{number:03d}.sql"
                path.write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")
                files.append(path)

            script = migrate_postgres.build_expand_script(files)

            self.assertEqual(3, script.count("pg_advisory_lock("))
            self.assertEqual(3, script.count("pg_advisory_unlock("))
            self.assertIn(f"pg_advisory_lock({migrate_postgres.MIGRATION_LOCK_KEY + 3});", script)
            self.assertIn(f"pg_advisory_lock({migrate_postgres.MIGRATION_LOCK_KEY + 4});", script)
            self.assertLess(script.rindex("pg_advisory_lock("), script.index("DO $regent_migration_validation$"))
            self.assertIn('history."Checksum" IS NULL', script)
            self.assertIn("IS DISTINCT FROM expected.checksum", script)
            self.assertIn("EXCEPT SELECT history.\"Version\"", script)
            self.assertIn("Newest migration % did not record schema version %", script)
            self.assertEqual(2, script.count("\\ir '"))

    def test_non_strict_history_validation_defers_checksum_sql_until_column_exists(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            files = []
            for number in (3, 4):
                path = root / f"controlplane-{number:03d}.sql"
                path.write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")
                files.append(path)

            script = migrate_postgres.history_validation_sql(files)

            self.assertIn("IF has_checksum THEN", script)
            self.assertIn("EXECUTE $regent_checksum_drift$", script)
            self.assertIn('history."Checksum" IS NOT NULL AND history."Checksum" <> expected.checksum', script)
            self.assertLess(script.index("IF has_checksum THEN"), script.index('history."Checksum"'))

            strict_script = migrate_postgres.history_validation_sql(files, strict=True)
            self.assertIn("IF has_checksum THEN", strict_script)
            self.assertIn("EXECUTE $regent_checksum_drift$", strict_script)
            self.assertIn('history."Checksum" IS DISTINCT FROM expected.checksum', strict_script)

    def test_manifest_validation_rejects_tampered_sql_before_execution(self):
        from migration_checksum_manifest import build_manifest, manifest_text

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for number in (3, 4):
                (root / f"controlplane-{number:03d}.sql").write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")
            manifest_path = root / "controlplane-checksums.json"
            manifest_path.write_text(manifest_text(build_manifest(root, versions=(3, 4))), encoding="utf-8")
            (root / "controlplane-004.sql").write_text("BEGIN;\nSELECT 'tampered';\nCOMMIT;\n", encoding="utf-8")

            with self.assertRaises(SystemExit):
                migrate_postgres.validated_checksums(
                    [root / "controlplane-003.sql", root / "controlplane-004.sql"], manifest_path
                )

    def test_manifest_validation_requires_the_exact_file_and_version_set(self):
        from migration_checksum_manifest import build_manifest, manifest_text

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for number in (3, 4, 5):
                (root / f"controlplane-{number:03d}.sql").write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")
            manifest_path = root / "controlplane-checksums.json"
            manifest_path.write_text(manifest_text(build_manifest(root, versions=(3, 4, 5))), encoding="utf-8")

            with self.assertRaises(SystemExit):
                migrate_postgres.validated_checksums(
                    [root / "controlplane-003.sql", root / "controlplane-004.sql"], manifest_path
                )

    def test_expand_dry_run_accepts_synthetic_manifest_and_reports_rls_limit(self):
        from migration_checksum_manifest import build_manifest, manifest_text

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "controlplane-003.sql").write_text(
                'BEGIN;\nCREATE TABLE "SyntheticThree" ("Id" integer PRIMARY KEY);\n'
                'ALTER TABLE "SyntheticThree" ENABLE ROW LEVEL SECURITY;\n'
                'ALTER TABLE "SyntheticThree" FORCE ROW LEVEL SECURITY;\nCOMMIT;\n',
                encoding="utf-8",
            )
            (root / "controlplane-004.sql").write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")
            (root / "controlplane-checksums.json").write_text(
                manifest_text(build_manifest(root, versions=(3, 4))), encoding="utf-8"
            )

            result = run_migration_script("--sql-dir", root, "--dry-run")

            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("controlplane-003.sql version=3", result.stdout)
            self.assertIn("forced_rls_declarations=1", result.stdout)
            self.assertIn("RLS declarations counted", result.stdout)
            self.assertIn(
                "exact RLS/policy validation is performed by PostgreSQL integration/startup checks",
                result.stdout,
            )
            self.assertNotIn("RLS declarations validated", result.stdout)

    def test_expand_dry_run_requires_checksum_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "controlplane-003.sql").write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")

            result = run_migration_script("--sql-dir", root, "--dry-run")

            self.assertNotEqual(0, result.returncode)
            self.assertIn("Migration checksum manifest is missing", result.stderr)
            self.assertNotIn("PASS:", result.stdout)

    def test_contract_dry_run_does_not_claim_manifest_validation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "controlplane-010-contract.sql").write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")

            result = run_migration_script("--sql-dir", root, "--phase", "contract", "--dry-run")

            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            pass_line = next(line for line in result.stdout.splitlines() if line.startswith("PASS:"))
            self.assertNotIn("manifest versions", pass_line)
            self.assertNotIn("SHA-256 checksums", pass_line)
            self.assertIn("RLS declarations counted", pass_line)

    def test_contract_script_uses_the_same_legacy_lock_keys(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "controlplane-010-contract.sql"
            path.write_text("BEGIN;\nCOMMIT;\n", encoding="utf-8")

            script = migrate_postgres.build_contract_script([path])

            self.assertIn(f"pg_advisory_lock({migrate_postgres.MIGRATION_LOCK_KEY + 10});", script)
            self.assertIn(f"pg_advisory_unlock({migrate_postgres.MIGRATION_LOCK_KEY + 10});", script)


if __name__ == "__main__":
    unittest.main()
