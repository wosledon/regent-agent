#!/usr/bin/env python3
import argparse
import os
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser(description="Check local package caches for offline readiness.")
    parser.add_argument("--require", action="store_true", help="Fail when no cache is available")
    args = parser.parse_args()
    candidates = [
        Path.home() / ".nuget" / "packages",
        Path.home() / ".local" / "share" / "pnpm" / "store",
        Path.home() / ".npm" / "_cacache",
        Path(os.environ.get("PNPM_HOME", Path.home() / ".local" / "share" / "pnpm")) / "store",
    ]
    available = [path for path in candidates if path.exists() and any(path.iterdir())]
    for path in available:
        print(f"AVAILABLE: {path}")
    if not available:
        print("MISSING: no populated NuGet, pnpm or npm cache")
        if args.require:
            raise SystemExit(1)
    else:
        print("PASS: offline cache prerequisites found")


if __name__ == "__main__":
    main()
