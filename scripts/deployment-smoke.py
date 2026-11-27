#!/usr/bin/env python3
"""Run the live deployment smoke and always purge its disposable tenant."""

import argparse
import datetime
import json
import os
import secrets
import subprocess
import sys
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))

from scenario_support import ApiClient, ScenarioFailure


def ok(results, step, assertion):
    results.append(f"{step}:HTTP 200;{assertion}")


def register_tenant(client):
    email = f"deploy-smoke-{uuid.uuid4().hex}@example.test"
    password = secrets.token_urlsafe(24)
    auth, _ = client.expect("live-deployment", "register", "POST", "/auth/register", {
        "email": email,
        "password": password,
        "displayName": "Deployment Smoke",
        "companyName": "Deployment Smoke Co",
    })
    return auth


def run_smoke_checks(client, auth, results):
    me, _ = client.expect("live-deployment", "auth-me", "GET", "/auth/me")
    access = me.get("effective_access") if isinstance(me, dict) else None
    required = {"can_read_chat", "can_read_collaboration", "collaboration_scope"}
    if not isinstance(access, dict) or not required <= set(access):
        raise AssertionError("Effective access is incomplete.")
    ok(results, "auth/me", "effective_access_present=true;can_read_chat=true;can_read_collaboration=true")

    compliance, _ = client.expect("live-deployment", "compliance-profile", "GET", "/v1/compliance/profile")
    if not isinstance(compliance, dict) or not compliance:
        raise AssertionError("Compliance profile is empty.")
    ok(results, "compliance/profile", "non_empty_json=true")

    employee, _ = client.expect("live-deployment", "company-start", "POST", "/v1/company/start")
    if not isinstance(employee, dict) or not employee:
        raise AssertionError("Company start did not return an employee.")
    ok(results, "company/start", "employee_created=true")

    created, _ = client.expect("live-deployment", "project-create", "POST", "/v1/projects", {
        "name": f"Deployment smoke {uuid.uuid4().hex[:8]}",
        "owner": "Deployment Smoke",
        "mission": "Deployment smoke validation",
        "targetDays": 7,
        "budgetMinor": 100000,
        "currency": "CNY",
    })
    project = created.get("project") if isinstance(created, dict) else None
    if not isinstance(project, dict) or not project.get("id"):
        raise AssertionError("Project creation did not return a project.")
    project_id = project["id"]

    delivery, _ = client.expect(
        "live-deployment", "delivery-get", "GET", f"/v1/projects/{project_id}/delivery"
    )
    if (not isinstance(delivery, dict) or delivery.get("projectId") != project_id
            or not isinstance(delivery.get("evidence"), list)):
        raise AssertionError("Delivery projection is invalid.")
    ok(results, "delivery GET", "project_match=true;evidence_array=true")

    graph, _ = client.expect("live-deployment", "collaboration-graph", "GET", "/v1/collaboration/graph")
    if (not isinstance(graph, dict) or not isinstance(graph.get("nodes"), list)
            or not isinstance(graph.get("edges"), list)):
        raise AssertionError("Collaboration graph is invalid.")
    ok(results, "collaboration/graph", "nodes_array=true;edges_array=true")

    conversations, _ = client.expect("live-deployment", "conversations-list", "GET", "/v1/conversations")
    if not isinstance(conversations, list):
        raise AssertionError("Conversations response is not a list.")
    ok(results, "conversations", "list=true")


def cleanup_tenant(client, tenant_id, container="regent-agent-controlplane-1"):
    try:
        state, _ = client.expect("cleanup", "lifecycle-state", "GET", "/v1/lifecycle")
    except ScenarioFailure as exc:
        if exc.status == 404:
            return "already_absent"
        raise
    if isinstance(state, dict) and state.get("isPurged"):
        return "already_purged"

    completed = subprocess.run(
        ["docker", "exec", container, "dotnet", "RegentAgent.ControlPlane.dll",
         "--purge-tenants", "PURGE", tenant_id],
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode == 0:
        return "purged"

    try:
        state, _ = client.expect("cleanup", "lifecycle-state-recheck", "GET", "/v1/lifecycle")
    except ScenarioFailure as exc:
        if exc.status == 404:
            return "already_absent"
        raise
    if isinstance(state, dict) and state.get("isPurged"):
        return "already_purged"
    raise RuntimeError(f"cleanup command exited with status {completed.returncode}")


def execute_with_cleanup(client, results, report, check=run_smoke_checks, cleanup=cleanup_tenant):
    tenant_id = None
    authenticated = False
    cleanup_status = "not_created"
    logout_status = "not_attempted"
    try:
        auth = register_tenant(client)
        tenant_id = auth.get("tenant_id") if isinstance(auth, dict) else None
        if not tenant_id:
            raise AssertionError("Registration did not return a tenant_id.")
        authenticated = True
        check(client, auth, results)
        report["status"] = "passed"
    except ScenarioFailure as exc:
        report["status"] = "failed"
        report["blocker"] = f"{exc.scenario}/{exc.step}:HTTP {exc.status};code={exc.code}"
    except Exception as exc:
        report["status"] = "failed"
        report["blocker"] = f"{type(exc).__name__}:{str(exc)[:160]}"
    finally:
        if tenant_id:
            try:
                cleanup_status = cleanup(client, tenant_id)
            except Exception as exc:
                cleanup_status = f"failed:{type(exc).__name__}:{str(exc)[:160]}"
        if authenticated:
            try:
                client.expect("live-deployment", "logout", "POST", "/auth/logout", status=204)
                logout_status = "HTTP 204;session_revoked=true"
            except Exception as exc:
                logout_status = f"failed:{type(exc).__name__}:{str(exc)[:160]}"
        report["cleanup"] = cleanup_status
        report["logout"] = logout_status

    if cleanup_status.startswith("failed") or logout_status.startswith("failed"):
        report["status"] = "failed"
        if not report.get("blocker"):
            report["blocker"] = f"cleanup={cleanup_status};logout={logout_status}"
    return report


def parse_args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default=os.environ.get("REGENT_BASE_URL", "http://127.0.0.1:5080"))
    parser.add_argument("--cleanup-container", default=os.environ.get("REGENT_CONTROLPLANE_CONTAINER", "regent-agent-controlplane-1"))
    parser.add_argument("--report", type=Path)
    parser.add_argument("--dry-run", action="store_true", help="validate the harness without network calls")
    return parser.parse_args()


def main():
    args = parse_args()
    if args.dry_run:
        print("PASS: dry-run made no network calls; run tests/scripts/test_deployment_smoke.py for cleanup proof")
        return 0

    stamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    report_path = args.report or ROOT / "backups" / "deployments" / f"smoke-{stamp}.json"
    results = []
    report = {"status": "failed", "smoke": results}
    execute_with_cleanup(
        ApiClient(args.base_url, "live-deployment-smoke"),
        results,
        report,
        cleanup=lambda client, tenant_id: cleanup_tenant(client, tenant_id, args.cleanup_container),
    )
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    os.chmod(report_path, 0o600)
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
