#!/usr/bin/env python3
import argparse
import hashlib
import json
import zipfile
from pathlib import Path, PurePosixPath


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify and restore a local checksummed backup.")
    parser.add_argument("backup", help="Backup .zip file")
    parser.add_argument("--target", required=True, help="Empty target directory")
    args = parser.parse_args()
    target = Path(args.target).resolve()
    if target.exists() and any(target.iterdir()):
        raise SystemExit("Target must be empty")
    target.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(args.backup) as archive:
        manifest = json.loads(archive.read("manifest.json"))
        for item in manifest:
            relative = PurePosixPath(item["path"])
            if relative.is_absolute() or ".." in relative.parts:
                raise SystemExit("Unsafe backup path")
            data = archive.read(item["path"])
            if len(data) != item["size"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
                raise SystemExit(f"Checksum mismatch: {item['path']}")
            output = target.joinpath(*relative.parts)
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(data)
    print(f"PASS: backup verified and restored to {target}")


if __name__ == "__main__":
    main()
