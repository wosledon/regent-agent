import importlib.util
from pathlib import Path
import sys
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
SPEC = importlib.util.spec_from_file_location("s01_delivery", ROOT / "scripts" / "scenarios" / "s01_delivery.py")
s01_delivery = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(s01_delivery)


class FakeApiClient:
    ARTIFACT = {
        "id": "11111111-1111-1111-1111-111111111111",
        "objectKey": "tenants/tenant-1/projects/project-1/artifacts/11111111-1111-1111-1111-111111111111",
        "hash": "a" * 64,
    }

    def __init__(self, user_id="owner-1"):
        self.user_id = user_id
        self.calls = []
        self.done_evidence = None
        self.review = None
        self.reviewer = None

    def register(self, scenario, label):
        return {"tenant_id": "tenant-1", "user_id": self.user_id}

    def start_company(self, scenario):
        return {"id": "controller-1", "displayName": "Controller"}

    def independent_approver(self, scenario):
        if self.reviewer is None:
            self.reviewer = FakeApiClient("reviewer-1")
        return self.reviewer

    def agent_headers(self, token):
        return {}

    def expect(self, scenario, step, method, path, body=None, status=200, headers=None):
        self.calls.append(step)
        responses = {
            "project-runtime-tasks": ({"tasks": [{}, {}, {}]}, {}),
            "claim-runtime-task": ({"assignmentToken": "token", "taskId": "task-1", "leaseToken": "lease"}, {}),
            "complete-runtime-task": (None, {}),
            "task-artifacts": ([self.ARTIFACT], {}),
            "delivery-after-runtime": ({"tasks": [{"id": "task-1", "status": "ReadyForReview"}]}, {}),
            "agent-submit-delivery-task": ({"success": True}, {}),
            "runtime-after-agent-submit": ({"tasks": [{"id": "task-1", "status": "InReview"}]}, {}),
            "employee-timeline": ({"entries": [{"taskId": "task-1", "status": "Done"}]}, {}),
            "audit-verify": ({"valid": True}, {}),
        }
        if step == "independent-review":
            self.review = body
            return {"reviewer": body["reviewer"]}, {}
        if step == "done-evidence":
            self.done_evidence = body
            return {}, {}
        return responses[step]


class DeliveryScenarioTests(unittest.TestCase):
    def test_done_evidence_uses_object_shaped_checks(self):
        client = FakeApiClient()
        with mock.patch.object(s01_delivery, "ApiClient", return_value=client), mock.patch.object(
            s01_delivery, "create_project_with_plan", return_value={"projectId": "project-1"}
        ):
            result = s01_delivery.run()

        self.assertEqual("task-1", result["taskId"])
        self.assertIn("task-artifacts", client.calls)
        self.assertIn("independent-review", client.reviewer.calls)
        self.assertIn("done-evidence", client.reviewer.calls)
        self.assertNotIn("independent-review", client.calls)
        self.assertNotIn("done-evidence", client.calls)
        self.assertEqual(client.reviewer.user_id, client.reviewer.review["reviewer"])
        self.assertEqual(client.reviewer.user_id, client.reviewer.done_evidence["validator"])
        self.assertEqual(
            [{"id": FakeApiClient.ARTIFACT["id"], "kind": "test", "location": FakeApiClient.ARTIFACT["objectKey"],
              "sha256": FakeApiClient.ARTIFACT["hash"]}],
            client.reviewer.done_evidence["artifacts"],
        )
        self.assertEqual(
            [{"name": "scenario test", "passed": True, "evidenceRef": "test:scenario-delivery"}],
            client.reviewer.done_evidence["testResults"],
        )
        self.assertEqual(
            [{"name": "tenant isolation", "passed": True, "evidenceRef": "test:tenant-isolation"}],
            client.reviewer.done_evidence["securityChecks"],
        )


if __name__ == "__main__":
    unittest.main()
