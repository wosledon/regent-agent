from scenario_support import ApiClient, ScenarioFailure, create_project_with_plan, hire_employee

CP = "http://localhost:5080"


def run():
    s = "S-03"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "offboard")
    tenant = auth["tenant_id"]
    cp.start_company(s)
    employee, _, _, _ = hire_employee(cp, s, "Departing Engineer")
    linked = create_project_with_plan(cp, s, employee["displayName"])
    claim, _ = cp.expect(s, "claim-task", "POST", "/internal/tasks/claim", headers={
        "X-Runner-Id": "scenario-offboard-runner", "X-Regent-Tenant": tenant
    })
    token = claim["assignmentToken"]
    marker = f"private-{claim['taskId']}"
    captured, _ = cp.expect(s, "capture-private-memory", "POST", "/agent/v1/tools/invoke", {
        "toolName": "memory.capture", "version": "1.0.0", "capability": "memory.write",
        "taskGrant": '{"capabilities":["memory.write"]}', "taint": "Low",
        "argumentsJson": '{"operation":"capture","scope":"personal","content":"' + marker + '","sourceRef":"task:' + claim['taskId'] + '","confidence":0.8,"taint":"trusted"}'
    }, headers=cp.agent_headers(token))
    if not captured["success"]:
        raise ScenarioFailure(s, "capture-private-memory", "Agent could not capture personal memory")
    approval = cp.create_approval(s, "employee.offboard", f"Offboard {employee['displayName']}")
    cp.expect(s, "offboard", "POST", f"/v1/employees/{employee['id']}/offboard", {"approvalId": approval["id"]}, status=204)
    detail, _ = cp.expect(s, "project-after-offboard", "GET", f"/v1/projects/{linked['projectId']}")
    task = next(item for item in detail["tasks"] if item["id"] == claim["taskId"])
    if task["status"] != "Interrupted":
        raise ScenarioFailure(s, "project-after-offboard", f"active task remained {task['status']} after offboarding")
    status, _, _ = cp.call("POST", f"/agent/v1/tasks/{claim['taskId']}/complete", {
        "leaseToken": claim["leaseToken"], "output": "stale", "status": "Done", "evidenceRefs": []
    }, headers=cp.agent_headers(token))
    if status not in (401, 403, 409):
        raise ScenarioFailure(s, "stale-completion", f"stale lease completion returned HTTP {status}", status)
    status, _, _ = cp.call("GET", "/agent/v1/me", headers={"Authorization": f"Bearer {token}"})
    if status != 403:
        raise ScenarioFailure(s, "terminated-agent", f"terminated employee Agent access returned HTTP {status}", status)
    status, _, claim_again = cp.call("POST", "/internal/tasks/claim", headers={
        "X-Runner-Id": "scenario-offboard-runner-2", "X-Regent-Tenant": tenant
    })
    if status != 204:
        raise ScenarioFailure(s, "reclaim", f"interrupted task was reclaimable, HTTP {status}", status)
    memories, _ = cp.expect(s, "owner-memory-search", "GET", f"/v1/memories?query={marker}")
    if memories:
        raise ScenarioFailure(s, "private-memory-isolation", "departed employee private memory remained visible")
    return {"tenantId": tenant, "employeeId": employee["id"], "taskId": claim["taskId"]}
