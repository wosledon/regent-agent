#!/usr/bin/env python3
import json
from pathlib import Path
from upgrade_plan import run_drills


def main():
    reports = run_drills()
    output = Path("data/upgrade-drill-report.json")
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"passed": all(item["passed"] for item in reports), "scenarios": reports}, indent=2) + "\n")
    if not all(item["passed"] for item in reports):
        raise SystemExit("Upgrade drill failed")
    for item in reports:
        print(f"PASS: {item['scenario']} recovery={item['recovery']}")


if __name__ == "__main__":
    main()
