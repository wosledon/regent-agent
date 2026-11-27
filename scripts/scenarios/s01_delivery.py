import json
import uuid
from scenario_support import ApiClient, ScenarioFailure, create_project_with_plan

CP = "http://localhost:5080"


def run():
    s = "S-01"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "delivery")
    tenant = auth["tenant_id"]
    controller = cp.start_company(s)
    linked = create_project_with_plan(cp, s, controller["displayName"])
    detail, _ = cp.expect(s, "project-runtime-tasks", "GET", f"/v1/projects/{linked['projectId']}")
    if len(detail["tasks"]) != 3:
        raise ScenarioFailure(s, "project-runtime-tasks", f"approved plan produced {len(detail['tasks'])} runtime tasks, expected 3")
    claim, _ = cp.expect(s, "claim-runtime-task", "POST", "/internal/tasks/claim", headers={
        "X-Runner-Id": "scenario-delivery-runner", "X-Regent-Tenant": tenant
    })
    token = claim["assignmentToken"]
    cp.expect(s, "complete-runtime-task", "POST", f"/agent/v1/tasks/{claim['taskId']}/complete", {
        "leaseToken": claim["leaseToken"], "output": "# Scenario delivery\n\nImplemented and verified.",
        "status": "Done", "evidenceRefs": ["test:scenario-delivery"]
    }, headers=cp.agent_headers(token), status=204)
    artifacts, _ = cp.expect(s, "task-artifacts", "GET",
        f"/v1/projects/{linked['projectId']}/delivery/tasks/{claim['taskId']}/artifacts")
    if not artifacts:
        raise ScenarioFailure(s, "task-artifacts", "runtime completion did not produce an authoritative artifact")
    artifact = artifacts[0]
    try:
        uuid.UUID(artifact["id"])
    except (KeyError, TypeError, ValueError, AttributeError) as error:
        raise ScenarioFailure(s, "task-artifacts", "artifact ID is not an authoritative GUID") from error
    if not artifact.get("objectKey") or len(artifact.get("hash", "")) != 64 or any(
        character not in "0123456789abcdefABCDEF" for character in artifact["hash"]
    ):
        raise ScenarioFailure(s, "task-artifacts", "artifact object key or SHA-256 is missing")
    delivery, _ = cp.expect(s, "delivery-after-runtime", "GET", f"/v1/projects/{linked['projectId']}/delivery")
    task = next(item for item in delivery["tasks"] if item["id"] == claim["taskId"])
    if task["status"] != "ReadyForReview":
        raise ScenarioFailure(s, "delivery-after-runtime", f"runtime completion left delivery task at {task['status']}, expected ReadyForReview")
    submitted, _ = cp.expect(s, "agent-submit-delivery-task", "POST", "/agent/v1/tools/invoke", {
        "toolName": "project.submit_task", "version": "1.0.0", "capability": "tasks:execute",
        "taskGrant": '{"capabilities":["tasks:execute"]}', "taint": "Low",
        "argumentsJson": json.dumps({
            "operation": "submit_task", "projectId": linked["projectId"], "taskId": claim["taskId"]
        })
    }, headers=cp.agent_headers(token))
    if not submitted["success"]:
        raise ScenarioFailure(s, "agent-submit-delivery-task", "Agent project Tool did not submit the task")
    detail, _ = cp.expect(s, "runtime-after-agent-submit", "GET", f"/v1/projects/{linked['projectId']}")
    if next(item for item in detail["tasks"] if item["id"] == claim["taskId"])["status"] != "InReview":
        raise ScenarioFailure(s, "runtime-after-agent-submit", "Agent submit Tool did not project InReview runtime status")
    reviewer = cp.independent_approver(s)
    if reviewer.user_id == auth["user_id"]:
        raise ScenarioFailure(s, "independent-review", "reviewer must use a distinct authenticated identity")
    review, _ = reviewer.expect(s, "independent-review", "POST", f"/v1/projects/{linked['projectId']}/delivery/reviews", {
        "subjectId": claim["taskId"], "subjectType": "Task", "reviewer": reviewer.user_id,
        "author": controller["displayName"], "verdict": "Approved", "findings": [],
        "evidence": [{"id": artifact["id"], "kind": "artifact", "location": artifact["objectKey"], "sha256": artifact["hash"]}]
    })
    if review["reviewer"] != reviewer.user_id:
        raise ScenarioFailure(s, "independent-review", "review identity was not taken from the authenticated reviewer")
    reviewer.expect(s, "done-evidence", "POST", f"/v1/projects/{linked['projectId']}/delivery/tasks/{claim['taskId']}/complete", {
        "workItemId": claim["taskId"], "workItemType": "Task", "criteriaResults": {"Tasks are claimable": True},
        "artifacts": [{"id": artifact["id"], "kind": "test", "location": artifact["objectKey"], "sha256": artifact["hash"]}],
        "testResults": [{"name": "scenario test", "passed": True, "evidenceRef": "test:scenario-delivery"}],
        "securityChecks": [{"name": "tenant isolation", "passed": True, "evidenceRef": "test:tenant-isolation"}],
        "openP0P1Defects": 0, "validator": reviewer.user_id
    })
    timeline, _ = cp.expect(s, "employee-timeline", "GET", f"/v1/employees/{controller['id']}/timeline")
    if not any(entry.get("taskId") == claim["taskId"] and entry["status"] == "Done" for entry in timeline["entries"]):
        raise ScenarioFailure(s, "employee-timeline", "completed runtime task did not produce a Done work entry")
    audit, _ = cp.expect(s, "audit-verify", "GET", "/v1/audit/verify")
    if not audit["valid"]:
        raise ScenarioFailure(s, "audit-verify", "audit hash chain is invalid after delivery")
    return {"tenantId": tenant, "projectId": linked["projectId"], "taskId": claim["taskId"], "runtimeTasks": 3}
