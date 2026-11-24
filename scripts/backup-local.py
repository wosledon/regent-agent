#!/usr/bin/env python3
import argparse
import hashlib
import json
import os
import zipfile
from pathlib import Path
from urllib.parse import urlparse


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    parser = argparse.ArgumentParser(description="Create a checksummed local backup without containers.")
    parser.add_argument("--source", default="src", help="Source tree containing service data directories")
    parser.add_argument("--output", required=True, help="Output .zip file")
    parser.add_argument("--target", choices=("local", "s3"), default="local", help="Optional offsite upload target")
    parser.add_argument("--target-uri", help="S3 URI such as s3://bucket/backups")
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
    if args.target == "s3":
        if not args.target_uri or not args.target_uri.startswith("s3://"):
            raise SystemExit("--target-uri must be an s3:// URI")
        try:
            import boto3
        except ImportError as exc:
            raise SystemExit("S3 backup target requires the optional boto3 package") from exc
        parsed = urlparse(args.target_uri)
        key = f"{parsed.path.strip('/')}/{output.name}".lstrip("/")
        client = boto3.client("s3", endpoint_url=os.environ.get("AWS_ENDPOINT_URL_S3") or None)
        client.upload_file(str(output), parsed.netloc, key)
        print(f"PASS: backup uploaded to s3://{parsed.netloc}/{key}")
    print(f"PASS: backup created with {len(files)} files at {output}")


if __name__ == "__main__":
    main()
