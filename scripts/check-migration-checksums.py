#!/usr/bin/env python3
"""Verify or regenerate the embedded ControlPlane migration checksum manifest."""
import argparse
from pathlib import Path

from migration_checksum_manifest import (
    build_manifest,
    load_manifest,
    manifest_text,
    validate_files_against_manifest,
)

ROOT = Path(__file__).resolve().parents[1]
SQL_DIR = ROOT / "deploy" / "sql"
MANIFEST = SQL_DIR / "controlplane-checksums.json"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true", help="regenerate the checked-in manifest")
    args = parser.parse_args()
    expected = manifest_text(build_manifest())
    if args.write:
        MANIFEST.write_text(expected, encoding="utf-8")
        print(f"WROTE: {MANIFEST.relative_to(ROOT)}")
        return
    try:
        manifest = load_manifest(MANIFEST)
        validate_files_against_manifest(
            [SQL_DIR / f"controlplane-{version:03d}.sql" for version in range(3, 10)], manifest
        )
    except ValueError as error:
        raise SystemExit(
            f"Migration checksum manifest is stale or invalid: {MANIFEST.relative_to(ROOT)}; "
            f"run python3 scripts/check-migration-checksums.py --write ({error})"
        ) from error
    actual = MANIFEST.read_text(encoding="utf-8")
    if actual != expected:
        raise SystemExit(
            f"Migration checksum manifest is stale: {MANIFEST.relative_to(ROOT)}; "
            "run python3 scripts/check-migration-checksums.py --write"
        )
    print("PASS: controlplane-003 through controlplane-009 match the checked-in SHA-256 manifest")


if __name__ == "__main__":
    main()
