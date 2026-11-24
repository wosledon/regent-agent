"""Deterministic upgrade planning and rollback-boundary drills."""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def validate_tag(tag):
    if not re.fullmatch(r"v\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?", tag):
        raise ValueError("Production versions must be immutable vMAJOR.MINOR.PATCH tags")


def build_plan(from_schema, to_schema, from_tag, to_tag, backup_enabled, risk_accepted_by):
    validate_tag(from_tag)
    validate_tag(to_tag)
    if from_schema < 0 or to_schema <= from_schema:
        raise ValueError("Target schema must be newer than the source schema")
    if to_schema - from_schema > 1:
        raise ValueError("Only N/N-1 mixed-version upgrades are supported in one drill")
    if not backup_enabled and not risk_accepted_by:
        raise ValueError("Disabled backups require an explicit risk acceptor")
    steps = [
        ("verify-release", "Verify Tag, commit, lockfiles, LicenseInventory and SBOM", True),
        ("license-and-vulnerability-gate", "Require zero blocked licenses and known vulnerabilities", True),
        ("offline-cache", "Validate source, image and package caches when OFFLINE=1", True),
        ("drain", "Stop new work and drain or checkpoint active tasks", True),
        ("backup", "Run enabled BackupProfile components" if backup_enabled else "Record backup_skipped risk acceptance", True),
        ("expand", "Apply additive Expand migration; old application remains compatible", True),
        ("deploy", "Deploy target Tag while previous image/Tag remains available", True),
        ("smoke", "Run health, identity, API, migration and read/write smoke checks", True),
        ("resume", "Resume drained work from checkpoints", False),
        ("contract", "Apply guarded Contract migration only after smoke passes", False),
    ]
    return {
        "from_tag": from_tag, "to_tag": to_tag, "from_schema": from_schema, "to_schema": to_schema,
        "compatibility": "N/N-1", "backup_enabled": backup_enabled, "risk_accepted_by": risk_accepted_by,
        "steps": [{"name": name, "description": description, "rollback_allowed": rollback} for name, description, rollback in steps],
        "rollback": {"before_contract": "Restart previous image ID or rebuild previous Tag; keep expanded schema",
                     "after_contract": "Application rollback forbidden; roll forward with a new corrective migration"},
    }


def run_drills():
    reports = []
    for scenario, failure_step in [("success", None), ("deploy-failure", "deploy"), ("smoke-failure", "smoke"), ("contract-failure", "contract")]:
        plan = build_plan(2, 3, "v0.0.2", "v0.0.3", True, None)
        reached = []
        failed = False
        for step in plan["steps"]:
            reached.append(step["name"])
            if step["name"] == failure_step:
                failed = True
                break
        contract_reached = "contract" in reached
        rollback_allowed = not contract_reached
        reports.append({"scenario": scenario, "failed_step": failure_step, "reached": reached,
                        "rollback_allowed": rollback_allowed,
                        "recovery": "restore-previous-image-or-tag" if rollback_allowed else "roll-forward-corrective-migration",
                        "passed": (scenario == "success" and not failed) or (scenario != "success" and failed)})
    return reports


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--from-schema", type=int, required=True)
    parser.add_argument("--to-schema", type=int, required=True)
    parser.add_argument("--from-tag", default="v0.0.2")
    parser.add_argument("--to-tag", default="v0.0.3")
    parser.add_argument("--backup-enabled", action="store_true")
    parser.add_argument("--risk-accepted-by")
    parser.add_argument("--output", default="data/upgrade-plan.json")
    args = parser.parse_args()
    plan = build_plan(args.from_schema, args.to_schema, args.from_tag, args.to_tag, args.backup_enabled, args.risk_accepted_by)
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(plan, indent=2) + "\n")
    print(f"PASS: upgrade plan {args.from_tag} -> {args.to_tag}, schema {args.from_schema} -> {args.to_schema}")


if __name__ == "__main__":
    main()
