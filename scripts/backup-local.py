#!/usr/bin/env python3
import argparse
import hashlib
import json
import zipfile
from pathlib import Path


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description="Create a checksummed local backup without containers.")
    parser.add_argument("--source", default="src", help="Source tree containing service data directories")
    parser.add_argument("--output", required=True, help="Output .zip file")
    args = parser.parse_args()
    source = Path(args.source).resolve()
    if not source.exists():
        raise SystemExit("Source does not exist")
    files = sorted(path for path in source.glob("*/data/**/*") if path.is_file())
    manifest = [{"path": str(path.relative_to(source)), "size": path.stat().st_size, "sha256": digest(path)} for path in files]
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, indent=2))
        for path in files:
            archive.write(path, str(path.relative_to(source)))
    print(f"PASS: backup created with {len(files)} files at {output}")


if __name__ == "__main__":
    main()
