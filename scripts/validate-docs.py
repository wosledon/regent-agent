from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
docs = root / "docs"
files = list(docs.rglob("*.md")) + [root / "AGENTS.md", root / "CONTRIBUTING.md"]

for path in files:
    text = path.read_text(encoding="utf-8")
    assert text.count("```") % 2 == 0, f"Unbalanced fences: {path}"
    for target in re.findall(r"\]\(([^)]+)\)", text):
        if "://" in target or target.startswith("#"):
            continue
        assert (path.parent / target.split("#", 1)[0]).resolve().exists(), f"Broken link: {path} -> {target}"
    lines = text.splitlines()
    i = 0
    while i < len(lines):
        if lines[i].startswith("|"):
            block = []
            while i < len(lines) and lines[i].startswith("|"):
                block.append(lines[i])
                i += 1
            assert len({line.count("|") for line in block}) == 1, f"Malformed table: {path}"
        else:
            i += 1

pending = (docs / "Regent-Agent-待确认项.md").read_text(encoding="utf-8")
ids = re.findall(r"^\| (Q-\d{3}) \|", pending, re.M)
assert ids[:52] == [f"Q-{i:03d}" for i in range(1, 53)]
assert ids[52:] == [f"Q-{i:03d}" for i in range(1, 52)] + ["Q-052"]

prd = (docs / "Regent-Agent-PRD.md").read_text(encoding="utf-8")
assert re.findall(r"^\| (FR-\d{3}) \|", prd, re.M) == [f"FR-{i:03d}" for i in range(1, 91)]
assert re.findall(r"^\| (BR-\d{3}) \|", prd, re.M) == [f"BR-{i:03d}" for i in range(1, 168)]
assert re.findall(r"^\| (G-\d{2}) \|", (docs / "design/README.md").read_text(encoding="utf-8"), re.M) == [f"G-{i:02d}" for i in range(1, 65)]

print("PASS: documentation links, fences, tables, and FR/BR/G/Q identifiers")
