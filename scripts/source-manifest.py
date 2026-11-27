#!/usr/bin/env python3
"""Hash release-critical tracked and untracked files without mutating Git state."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re

SCHEMA = "regent-source-manifest/v1"
HASH_ALGORITHM = "sha256"
SHA256 = re.compile(r"[0-9a-f]{64}\Z")
EXCLUDED_DIRECTORIES = {
    ".git", ".idea", ".secrets", ".stepcode", ".vs", "__pycache__", "artifacts", "bin", "coverage",
    "data", "dist", "node_modules", "obj", "playwright-report", "publish", "secrets", "test-results", "TestResults",
}
EXCLUDED_FILES = {".netrc", ".npmrc", "compose.override.yaml", "id_ed25519", "id_rsa"}
EXCLUDED_SUFFIXES = (
    ".db", ".key", ".log", ".p12", ".pem", ".pfx", ".pyc", ".secret", ".secrets", ".sqlite", ".sqlite3",
    ".tmp", ".tsbuildinfo",
)


def relative_name(path, root):
    return path.resolve().relative_to(root).as_posix()


def excluded(relative, output=None):
    if relative == output:
        return True
    parts = relative.split("/")
    if any(part in EXCLUDED_DIRECTORIES for part in parts[:-1]):
        return True
    name = parts[-1]
    if name in EXCLUDED_FILES or name.endswith(EXCLUDED_SUFFIXES):
        return True
    return name == ".env" or (name.startswith(".env.") and name != ".env.example")


def discover_files(root, output=None):
    root = Path(root).resolve()
    paths = []
    for directory, directory_names, file_names in os.walk(root, topdown=True, followlinks=False):
        current = Path(directory)
        directory_names[:] = sorted(
            name for name in directory_names
            if name not in EXCLUDED_DIRECTORIES and not (current / name).is_symlink()
        )
        for name in sorted(file_names):
            path = current / name
            relative = relative_name(path, root)
            if excluded(relative, output):
                continue
            if path.is_symlink() or not path.is_file():
                raise ValueError(f"Release manifest requires regular files: {relative}")
            paths.append(relative)
    return sorted(paths)


def hash_file(path):
    digest = hashlib.sha256()
    size = 0
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            digest.update(chunk)
            size += len(chunk)
    return size, digest.hexdigest()


def build_manifest(root, output=None):
    root = Path(root).resolve()
    files = []
    for relative in discover_files(root, output):
        size, checksum = hash_file(root / relative)
        files.append({"path": relative, "bytes": size, "sha256": checksum})
    canonical = json.dumps(
        [[item["path"], item["bytes"], item["sha256"]] for item in files],
        ensure_ascii=False, separators=(",", ":"),
    ).encode("utf-8")
    return {
        "schema": SCHEMA,
        "hash_algorithm": HASH_ALGORITHM,
        "file_count": len(files),
        "tree_sha256": hashlib.sha256(canonical).hexdigest(),
        "exclusions": {
            "directories": sorted(EXCLUDED_DIRECTORIES),
            "files": sorted(EXCLUDED_FILES),
            "suffixes": list(EXCLUDED_SUFFIXES),
        },
        "files": files,
    }


def manifest_text(manifest):
    return json.dumps(manifest, indent=2, ensure_ascii=False) + "\n"


def validate_manifest(manifest):
    if not isinstance(manifest, dict) or manifest.get("schema") != SCHEMA:
        raise ValueError("Source manifest schema is unsupported.")
    if manifest.get("hash_algorithm") != HASH_ALGORITHM:
        raise ValueError("Source manifest hash algorithm is unsupported.")
    files = manifest.get("files")
    if not isinstance(files, list) or manifest.get("file_count") != len(files):
        raise ValueError("Source manifest file count is invalid.")
    if not isinstance(manifest.get("tree_sha256"), str) or SHA256.fullmatch(manifest["tree_sha256"]) is None:
        raise ValueError("Source manifest tree SHA-256 is invalid.")
    seen = set()
    for item in files:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise ValueError("Source manifest file entry is invalid.")
        path = item["path"]
        if path in seen or path.startswith("/") or ".." in Path(path).parts:
            raise ValueError("Source manifest paths must be unique repository-relative POSIX paths.")
        seen.add(path)
        if isinstance(item.get("bytes"), bool) or not isinstance(item.get("bytes"), int) or item["bytes"] < 0:
            raise ValueError("Source manifest file size is invalid.")
        if not isinstance(item.get("sha256"), str) or SHA256.fullmatch(item["sha256"]) is None:
            raise ValueError("Source manifest file SHA-256 is invalid.")
    if [item["path"] for item in files] != sorted(seen):
        raise ValueError("Source manifest files must be sorted by path.")


def check_manifest(root, manifest_path):
    manifest_path = Path(manifest_path).resolve()
    try:
        expected = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"Source manifest is unreadable: {manifest_path}: {error}") from error
    validate_manifest(expected)
    root = Path(root).resolve()
    try:
        output = relative_name(manifest_path, root)
    except ValueError:
        output = None
    actual = build_manifest(root, output)
    if expected != actual:
        raise ValueError(
            "Source manifest does not match the current tree "
            f"(expected {expected['file_count']} files/{expected['tree_sha256']}, "
            f"actual {actual['file_count']} files/{actual['tree_sha256']})."
        )
    return actual


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, help="write the manifest to this path")
    parser.add_argument("--check", type=Path, help="verify an existing manifest against the current tree")
    args = parser.parse_args()
    if args.output and args.check:
        parser.error("--output and --check are mutually exclusive")
    root = args.root.resolve()
    if args.check:
        manifest = check_manifest(root, args.check)
    else:
        output = args.output.resolve() if args.output else root / "source-manifest.json"
        try:
            output_relative = relative_name(output, root)
        except ValueError:
            output_relative = None
        manifest = build_manifest(root, output_relative)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(manifest_text(manifest), encoding="utf-8")
    print(f"PASS: source manifest {manifest['file_count']} files, tree sha256 {manifest['tree_sha256']}")


if __name__ == "__main__":
    main()
