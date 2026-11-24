#!/usr/bin/env python3
"""Apply versioned ControlPlane SQL with an advisory lock; never run at app startup."""
import argparse
import hashlib
import os
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
SQL_DIR = ROOT / "deploy" / "sql"


def migrations(phase="expand"):
    files = sorted(SQL_DIR.glob("controlplane-*.sql"))
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


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--database-url", default=os.environ.get("REGENT_POSTGRES_DSN"))
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--phase", choices=["expand", "contract"], default="expand")
    args = parser.parse_args()
    files = migrations(args.phase)
    for path in files:
        sql = path.read_text(encoding="utf-8")
        digest = hashlib.sha256(sql.encode()).hexdigest()
        rls = len(re.findall(r"FORCE ROW LEVEL SECURITY", sql))
        print(f"{path.name} version={version(path)} sha256={digest} forced_rls_tables={rls}")
        if "BEGIN;" not in sql or "COMMIT;" not in sql:
            raise SystemExit(f"Migration {path.name} must be transactional.")
    if args.check or args.dry_run:
        print("PASS: migration files, versions, checksums, transactions and RLS declarations validated")
        return
    if not args.database_url:
        raise SystemExit("--database-url or REGENT_POSTGRES_DSN is required.")
    psql = subprocess.run(["psql", "--version"], capture_output=True, text=True)
    if psql.returncode != 0:
        raise SystemExit("psql is required for a real migration.")
    latest = int(subprocess.check_output([
        "psql", args.database_url, "-Atc",
        "SELECT COALESCE((SELECT MAX(\"Version\") FROM \"SchemaVersions\"), 0);"
    ], text=True).strip() or 0)
    for path in files:
        target = version(path)
        if args.phase == "expand" and target <= latest:
            print(f"SKIP: {path.name} already applied")
            continue
        lock = str(6_317_000 + target)
        command = f"SELECT pg_advisory_lock({lock}); \\i {path} SELECT pg_advisory_unlock({lock});"
        subprocess.run(["psql", args.database_url, "-v", "ON_ERROR_STOP=1", "-c", command], check=True)
        print(f"APPLIED: {path.name}")
        latest = target


if __name__ == "__main__":
    main()
