import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("source_manifest", ROOT / "scripts" / "source-manifest.py")
source_manifest = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(source_manifest)


class SourceManifestTests(unittest.TestCase):
    def test_manifest_includes_source_and_excludes_local_generated_and_secret_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            files = {
                "src/App.cs": "source",
                "tests/AppTests.cs": "tests",
                "contracts/openapi/controlplane.json": "{}",
                "deploy/sql/controlplane-009.sql": "SELECT 1;",
                "scripts/tool.py": "print('ok')",
                "pnpm-lock.yaml": "lockfile",
                ".env.example": "EXAMPLE=value",
                ".env": "SECRET=value",
                ".npmrc": "//registry/:_authToken=secret",
                "secrets/token.key": "secret",
                "compose.override.yaml": "local",
                "node_modules/package/index.js": "generated",
                "data/runtime.json": "runtime",
                "source-manifest.json": "old artifact",
            }
            for name, content in files.items():
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")

            manifest = source_manifest.build_manifest(root, "source-manifest.json")

            self.assertEqual([
                ".env.example", "contracts/openapi/controlplane.json", "deploy/sql/controlplane-009.sql",
                "pnpm-lock.yaml", "scripts/tool.py", "src/App.cs", "tests/AppTests.cs",
            ], [item["path"] for item in manifest["files"]])
            self.assertEqual(7, manifest["file_count"])
            self.assertEqual(64, len(manifest["tree_sha256"]))

    def test_manifest_is_deterministic_and_content_addressable(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "b.txt").write_text("b", encoding="utf-8")
            (root / "a.txt").write_text("a", encoding="utf-8")
            first = source_manifest.build_manifest(root)
            second = source_manifest.build_manifest(root)
            self.assertEqual(first, second)

            (root / "a.txt").write_text("changed", encoding="utf-8")
            changed = source_manifest.build_manifest(root)
            self.assertNotEqual(first["tree_sha256"], changed["tree_sha256"])
            self.assertNotEqual(first["files"][0]["sha256"], changed["files"][0]["sha256"])

    def test_check_manifest_rejects_tree_changes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source.cs").write_text("before", encoding="utf-8")
            output = root / "source-manifest.json"
            output.write_text(source_manifest.manifest_text(source_manifest.build_manifest(root, output.name)),
                encoding="utf-8")
            self.assertEqual(1, source_manifest.check_manifest(root, output)["file_count"])

            (root / "source.cs").write_text("after", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "does not match"):
                source_manifest.check_manifest(root, output)

    def test_manifest_json_is_stable(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source.cs").write_text("source", encoding="utf-8")
            manifest = source_manifest.build_manifest(root)
            encoded = source_manifest.manifest_text(manifest)
            self.assertEqual(encoded, json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")


if __name__ == "__main__":
    unittest.main()
