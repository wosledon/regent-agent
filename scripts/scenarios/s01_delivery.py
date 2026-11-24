import json
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
    cp.expect(s, "independent-review", "POST", f"/v1/projects/{linked['projectId']}/delivery/reviews", {
        "subjectId": claim["taskId"], "subjectType": "Task", "reviewer": "Independent Reviewer",
        "author": controller["displayName"], "verdict": "Approved", "findings": [],
        "evidence": [{"id": "review:scenario", "kind": "test", "location": "local:scenario"}]
    })
    cp.expect(s, "done-evidence", "POST", f"/v1/projects/{linked['projectId']}/delivery/tasks/{claim['taskId']}/complete", {
        "workItemId": claim["taskId"], "workItemType": "Task", "criteriaResults": {"Tasks are claimable": True},
        "artifacts": [{"id": "artifact:scenario", "kind": "test", "location": "local:scenario"}],
        "testResults": ["scenario test passed"], "securityChecks": ["tenant isolation checked"],
        "openP0P1Defects": 0, "validator": "Independent Reviewer"
    })
    timeline, _ = cp.expect(s, "employee-timeline", "GET", f"/v1/employees/{controller['id']}/timeline")
    if not any(entry.get("taskId") == claim["taskId"] and entry["status"] == "Done" for entry in timeline["entries"]):
        raise ScenarioFailure(s, "employee-timeline", "completed runtime task did not produce a Done work entry")
    audit, _ = cp.expect(s, "audit-verify", "GET", "/v1/audit/verify")
    if not audit["valid"]:
        raise ScenarioFailure(s, "audit-verify", "audit hash chain is invalid after delivery")
    return {"tenantId": tenant, "projectId": linked["projectId"], "taskId": claim["taskId"], "runtimeTasks": 3}
