#!/usr/bin/env python3
"""Fail the release gate on known NuGet or production pnpm vulnerabilities."""
import argparse
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def run(command):
    return subprocess.run(command, cwd=ROOT, capture_output=True, text=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", default="data/supply-chain-vulnerabilities.json")
    args = parser.parse_args()
    findings = []
    nuget = run(["dotnet", "list", "RegentAgent.slnx", "package", "--vulnerable", "--include-transitive", "--format", "json"])
    if nuget.returncode != 0:
        raise SystemExit(nuget.stderr or nuget.stdout)
    for project in json.loads(nuget.stdout).get("projects", []):
        for framework in project.get("frameworks", []):
            for vulnerability in framework.get("vulnerabilities", []):
                findings.append({"ecosystem": "nuget", "project": project.get("path"), "name": vulnerability.get("name"),
                                 "severity": vulnerability.get("severity"), "advisory": vulnerability.get("advisory")})
    npm = run(["corepack", "pnpm", "audit", "--prod", "--json", "--registry=https://registry.npmjs.org"])
    try:
        audit = json.loads(npm.stdout)
    except json.JSONDecodeError:
        raise SystemExit(npm.stderr or "pnpm audit did not return JSON.")
    for advisory in audit.get("advisories", {}).values():
        findings.append({"ecosystem": "npm", "name": advisory.get("module_name"), "severity": advisory.get("severity"),
                         "advisory": advisory.get("github_advisory_id") or advisory.get("url")})
    report = {"passed": not findings, "findings": findings, "nuget_source": "https://api.nuget.org/v3/index.json",
              "npm_registry": "https://registry.npmjs.org"}
    output = Path(args.report)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n")
    if findings:
        for item in findings:
            print(f"VULNERABLE: {item['ecosystem']} {item['name']} severity={item['severity']} advisory={item['advisory']}")
        raise SystemExit(1)
    print("PASS: 0 known NuGet or production pnpm vulnerabilities")


if __name__ == "__main__":
    main()
