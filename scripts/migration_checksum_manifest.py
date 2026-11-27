"""Shared parsing and validation for ControlPlane migration checksum manifests."""
import hashlib
import json
from pathlib import Path
import re

SCHEMA = "regent-controlplane-migration-checksums/v1"
VERSIONS = range(3, 10)
FILE_NAME = re.compile(r"controlplane-(\d{3})\.sql\Z")
SHA256 = re.compile(r"[0-9a-f]{64}\Z")


def file_version(path):
    match = FILE_NAME.fullmatch(Path(path).name)
    if match is None:
        raise ValueError(f"Invalid migration file name: {Path(path).name}")
    return int(match.group(1))


def file_checksum(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def build_manifest(sql_dir=None, versions=VERSIONS):
    if sql_dir is None:
        sql_dir = Path(__file__).resolve().parents[1] / "deploy" / "sql"
    migrations = []
    for version in versions:
        path = Path(sql_dir) / f"controlplane-{version:03d}.sql"
        migrations.append({
            "version": version,
            "file": path.name,
            "sha256": file_checksum(path),
        })
    return {"schema": SCHEMA, "migrations": migrations}


def manifest_text(manifest):
    return json.dumps(manifest, indent=2) + "\n"


def load_manifest(path):
    manifest_path = Path(path)
    if not manifest_path.exists():
        raise ValueError(f"Migration checksum manifest is missing: {manifest_path}")
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"Migration checksum manifest is unreadable: {manifest_path}: {error}") from error
    validate_manifest_shape(manifest)
    return manifest


def validate_manifest_shape(manifest):
    if not isinstance(manifest, dict) or manifest.get("schema") != SCHEMA:
        raise ValueError("Migration checksum manifest schema is unsupported.")
    migrations = manifest.get("migrations")
    if not isinstance(migrations, list) or not migrations:
        raise ValueError("Migration checksum manifest must contain a non-empty migrations array.")
    seen = set()
    for migration in migrations:
        if not isinstance(migration, dict):
            raise ValueError("Migration checksum manifest entries must be objects.")
        version = migration.get("version")
        file = migration.get("file")
        checksum = migration.get("sha256")
        if isinstance(version, bool) or not isinstance(version, int):
            raise ValueError("Migration checksum manifest versions must be integers.")
        if file != f"controlplane-{version:03d}.sql":
            raise ValueError("Migration checksum manifest file names must match their versions.")
        if not isinstance(checksum, str) or SHA256.fullmatch(checksum) is None:
            raise ValueError("Migration checksum manifest SHA-256 values must be lowercase 64-character hex.")
        if version in seen:
            raise ValueError("Migration checksum manifest contains a duplicate version.")
        seen.add(version)


def validate_files_against_manifest(files, manifest):
    validate_manifest_shape(manifest)
    actual = {file_version(path): (Path(path).name, file_checksum(path)) for path in files}
    expected = {
        migration["version"]: (migration["file"], migration["sha256"])
        for migration in manifest["migrations"]
    }
    if actual.keys() != expected.keys():
        missing = sorted(expected.keys() - actual.keys())
        unexpected = sorted(actual.keys() - expected.keys())
        raise ValueError(
            "Migration files must exactly match the checksum manifest versions; "
            f"missing={missing}, unexpected={unexpected}."
        )
    for version, (file, checksum) in actual.items():
        expected_file, expected_checksum = expected[version]
        if file != expected_file or checksum != expected_checksum:
            raise ValueError(
                f"Migration {file} does not match the checksum manifest "
                f"(expected {expected_checksum}, actual {checksum})."
            )
    return expected
