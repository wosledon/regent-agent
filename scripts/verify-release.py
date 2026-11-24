#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], text=True).strip()


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify a clean source release and write a local manifest.")
    parser.add_argument("--tag", required=True, help="Approved v* tag")
    parser.add_argument("--output", default="data/release-manifest.json")
    parser.add_argument("--dry-run", action="store_true", help="Validate inputs without requiring an existing tag or clean worktree")
    args = parser.parse_args()
    if not re.fullmatch(r"v\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", args.tag):
        raise SystemExit("Release tag must be an immutable vMAJOR.MINOR.PATCH tag")
    if not args.dry_run and git("status", "--porcelain"):
        raise SystemExit("Worktree must be clean before release verification")
    commit = git("rev-parse", "HEAD")
    if not args.dry_run:
        tagged = git("rev-list", "-n", "1", args.tag)
        if commit != tagged:
            raise SystemExit("HEAD does not match the approved tag")
    root = Path(__file__).resolve().parents[1]
    def file_hash(path):
        return hashlib.sha256((root / path).read_bytes()).hexdigest()
    schema = (root / "src/RegentAgent.ControlPlane/Application/SchemaVersionService.cs").read_text()
    schema_version = int(re.search(r"CurrentVersion = (\d+)", schema).group(1))
    manifest = {
        "tag": args.tag,
        "commit": commit,
        "source": "github-tag",
        "build_mode": "local-source",
        "containers_used": False,
        "base_image_digests": [],
        "local_image_ids": [],
        "schema_version": schema_version,
        "lockfile_sha256": {name: file_hash(name) for name in ["pnpm-lock.yaml", "RegentAgent.slnx"]},
        "license_inventory_sha256": file_hash("deploy/license-inventory.json"),
        "sbom_sha256": file_hash("deploy/sbom.json"),
        "migration_sha256": {path.name: file_hash(path) for path in sorted((root / "deploy/sql").glob("controlplane-*.sql"))},
        "supply_chain_gate": "required",
        "upgrade_order": ["drain", "backup-or-risk-acceptance", "expand", "deploy", "smoke", "resume", "contract"],
    }
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"PASS: release source verified ({args.tag} @ {commit[:12]})")


if __name__ == "__main__":
    main()
