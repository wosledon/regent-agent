#!/usr/bin/env python3
"""Require third-party GitHub Actions to be pinned to immutable commit SHAs."""
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
errors = []
for path in (root / ".github" / "workflows").glob("*.y*ml"):
    for line_number, line in enumerate(path.read_text().splitlines(), 1):
        match = re.search(r"uses:\s*([^\s#]+)", line)
        if not match:
            continue
        reference = match.group(1)
        if reference.startswith("./") or reference.startswith("docker://"):
            continue
        if not re.search(r"@[0-9a-f]{40}$", reference, re.I):
            errors.append(f"{path.relative_to(root)}:{line_number}: {reference}")
if errors:
    print("\n".join(errors), file=sys.stderr)
    raise SystemExit("Third-party Actions must use full commit SHA pins")
print("PASS: all third-party GitHub Actions are pinned to commit SHAs")
