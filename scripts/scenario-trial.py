#!/usr/bin/env python3
"""Run local cross-feature scenarios and write a redacted JSON evidence report."""
import argparse
import importlib
import json
from pathlib import Path
import sys
import traceback

SCENARIOS = [
    ("S-01", "s01_delivery", "交付计划到运行时任务"),
    ("S-02", "s02_recruitment", "招聘录用到套餐配额"),
    ("S-03", "s03_offboard", "在途任务员工离职"),
    ("S-04", "s04_memory", "记忆投毒与独立纠错"),
    ("S-05", "s05_budget", "模型调用到金额结算"),
    ("S-06", "s06_lifecycle", "租户软删除与恢复"),
    ("S-07", "s07_billing", "套餐配额与本地支付"),
    ("S-08", "s08_oidc", "required SSO 与紧急恢复"),
    ("S-09", "s09_observability", "遥测告警到行动中心"),
    ("S-10", "s10_backup", "备份作业到双人恢复"),
]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--only", help="Run one scenario ID, for example S-01")
    parser.add_argument("--output", default="data/scenario-trial-report.json")
    args = parser.parse_args()
    selected = [item for item in SCENARIOS if args.only is None or item[0] == args.only]
    if not selected:
        raise SystemExit("Unknown scenario id")
    report = []
    for scenario_id, module_name, title in selected:
        try:
            module = importlib.import_module(f"scenarios.{module_name}")
            evidence = module.run()
            report.append({"id": scenario_id, "title": title, "status": "passed", "evidence": evidence})
            print(f"PASS: {scenario_id} {title}")
        except Exception as exception:
            report.append({"id": scenario_id, "title": title, "status": "failed",
                           "error": str(exception), "type": type(exception).__name__})
            print(f"FAIL: {scenario_id} {title}: {exception}")
            if args.only:
                traceback.print_exc()
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({"passed": all(item["status"] == "passed" for item in report),
                                  "scenarios": report}, ensure_ascii=False, indent=2) + "\n")
    print(f"Report: {output}")
    if not all(item["status"] == "passed" for item in report):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
