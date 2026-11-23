#!/usr/bin/env python3
import argparse
import json
import subprocess
from pathlib import Path


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], text=True).strip()


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify a clean source release and write a local manifest.")
    parser.add_argument("--tag", required=True, help="Approved v* tag")
    parser.add_argument("--output", default="data/release-manifest.json")
    args = parser.parse_args()
    if not args.tag.startswith("v"):
        raise SystemExit("Release tag must start with v")
    if git("status", "--porcelain"):
        raise SystemExit("Worktree must be clean before release verification")
    commit = git("rev-parse", "HEAD")
    tagged = git("rev-list", "-n", "1", args.tag)
    if commit != tagged:
        raise SystemExit("HEAD does not match the approved tag")
    manifest = {
        "tag": args.tag,
        "commit": commit,
        "source": "github-tag",
        "build_mode": "local-source",
        "containers_used": False,
        "base_image_digests": [],
        "local_image_ids": [],
    }
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"PASS: release source verified ({args.tag} @ {commit[:12]})")


if __name__ == "__main__":
    main()
