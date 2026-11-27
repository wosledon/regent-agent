import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("deployment_smoke", ROOT / "scripts" / "deployment-smoke.py")
deployment_smoke = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(deployment_smoke)

TENANT_ID = "11111111-1111-1111-1111-111111111111"


class FakeClient:
    def __init__(self, lifecycle=None):
        self.lifecycle = lifecycle or {"isPurged": False, "deletionRequests": []}
        self.calls = []

    def expect(self, scenario, step, method, path, body=None, status=200, headers=None):
        self.calls.append((method, path, body, status))
        if path == "/auth/register":
            return {"tenant_id": TENANT_ID}, {}
        if path == "/v1/lifecycle":
            return self.lifecycle, {}
        if path == "/auth/logout":
            return None, {}
        raise AssertionError(f"Unexpected request: {method} {path}")


class DeploymentSmokeTests(unittest.TestCase):
    def test_cleanup_runs_after_success(self):
        client = FakeClient()
        cleanup_calls = []

        def cleanup(_client, tenant_id):
            cleanup_calls.append(tenant_id)
            return "purged"

        report = deployment_smoke.execute_with_cleanup(
            client, [], {"status": "failed", "smoke": []},
            check=lambda _client, _auth, _results: None,
            cleanup=cleanup,
        )

        self.assertEqual("passed", report["status"])
        self.assertEqual("purged", report["cleanup"])
        self.assertEqual([TENANT_ID], cleanup_calls)

    def test_cleanup_runs_after_failure(self):
        client = FakeClient()
        cleanup_calls = []

        def cleanup(_client, tenant_id):
            cleanup_calls.append(tenant_id)
            return "purged"

        def fail(_client, _auth, _results):
            raise deployment_smoke.ScenarioFailure("live-deployment", "forced", "forced failure", 500, "forced")

        report = deployment_smoke.execute_with_cleanup(
            client, [], {"status": "failed", "smoke": []},
            check=fail,
            cleanup=cleanup,
        )

        self.assertEqual("failed", report["status"])
        self.assertEqual("purged", report["cleanup"])
        self.assertEqual([TENANT_ID], cleanup_calls)

    def test_cleanup_is_idempotent_when_tenant_is_already_purged(self):
        client = FakeClient({"isPurged": True, "deletionRequests": []})
        with patch.object(deployment_smoke.subprocess, "run") as run:
            status = deployment_smoke.cleanup_tenant(client, TENANT_ID, "controlplane")
        self.assertEqual("already_purged", status)
        run.assert_not_called()

    def test_cleanup_uses_immediate_admin_purge(self):
        client = FakeClient()
        completed = type("Completed", (), {"returncode": 0})()
        with patch.object(deployment_smoke.subprocess, "run", return_value=completed) as run:
            status = deployment_smoke.cleanup_tenant(client, TENANT_ID, "controlplane")
        self.assertEqual("purged", status)
        run.assert_called_once_with(
            ["docker", "exec", "controlplane", "dotnet", "RegentAgent.ControlPlane.dll",
             "--purge-tenants", "PURGE", TENANT_ID],
            capture_output=True,
            text=True,
            check=False,
        )


if __name__ == "__main__":
    unittest.main()
