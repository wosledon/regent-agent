import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location(
    "check_migration_checksums", ROOT / "scripts" / "check-migration-checksums.py"
)
check_migration_checksums = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(check_migration_checksums)


class MigrationChecksumManifestTests(unittest.TestCase):
    def test_checked_in_manifest_matches_sql_files(self):
        expected = check_migration_checksums.manifest_text(
            check_migration_checksums.build_manifest()
        )

        self.assertEqual(expected, check_migration_checksums.MANIFEST.read_text(encoding="utf-8"))

    def test_manifest_tracks_every_required_migration(self):
        manifest = check_migration_checksums.build_manifest()

        self.assertEqual(list(range(3, 10)), [item["version"] for item in manifest["migrations"]])
        self.assertTrue(all(len(item["sha256"]) == 64 for item in manifest["migrations"]))
        self.assertTrue(all(item["file"] == f"controlplane-{item['version']:03d}.sql"
                            for item in manifest["migrations"]))

    def test_changed_sql_changes_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for version in range(3, 10):
                (root / f"controlplane-{version:03d}.sql").write_text(
                    f"SELECT {version};\n", encoding="utf-8"
                )
            before = check_migration_checksums.build_manifest(root)
            (root / "controlplane-007.sql").write_text("SELECT 8;\n", encoding="utf-8")
            after = check_migration_checksums.build_manifest(root)

            self.assertNotEqual(before["migrations"][4]["sha256"], after["migrations"][4]["sha256"])


if __name__ == "__main__":
    unittest.main()
