#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import tempfile
import zipfile
from pathlib import Path, PurePosixPath
from urllib.parse import urlparse


def restore(backup: Path, target: Path) -> None:
    if target.exists() and any(target.iterdir()):
        raise SystemExit("Target must be empty")
    target.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(backup) as archive:
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


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify and restore a local or S3 checksummed backup.")
    parser.add_argument("backup", help="Backup .zip file or s3://bucket/key URI")
    parser.add_argument("--target", required=True, help="Empty target directory")
    args = parser.parse_args()
    target = Path(args.target).resolve()
    if args.backup.startswith("s3://"):
        try:
            import boto3
        except ImportError as exc:
            raise SystemExit("S3 restore requires the optional boto3 package") from exc
        parsed = urlparse(args.backup)
        with tempfile.TemporaryDirectory(prefix="regent-restore-") as directory:
            backup = Path(directory) / "backup.zip"
            client = boto3.client("s3", endpoint_url=os.environ.get("AWS_ENDPOINT_URL_S3") or None)
            client.download_file(parsed.netloc, parsed.path.lstrip("/"), str(backup))
            restore(backup, target)
    else:
        restore(Path(args.backup), target)
    print(f"PASS: backup verified and restored to {target}")


if __name__ == "__main__":
    main()
