import uuid
from scenario_support import ApiClient, ScenarioFailure, create_project_with_plan

CP = "http://localhost:5080"


def run():
    s = "S-02"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "recruitment")
    tenant = auth["tenant_id"]
    cp.start_company(s)
    hired = []
    blocked_candidate = None
    for index in range(1, 6):
        name = f"Scenario Employee {index}"
        position, _ = cp.expect(s, f"position-{index}", "POST", "/v1/positions", {
            "id": str(uuid.uuid4()), "name": f"Scenario Position {index}", "responsibilities": "Execute scenario work",
            "capabilities": ["code.implement"], "budgetMinor": 0, "parentPositionId": None,
            "status": "Open", "version": 1
        })
        candidate, _ = cp.expect(s, f"candidate-{index}", "POST", "/v1/candidates", {
            "positionId": position["id"], "displayName": name, "modelVersion": "local-cheap-v1",
            "jobDescription": "Execute and verify deterministic delivery."
        })
        cp.expect(s, f"assess-{index}", "POST", f"/v1/candidates/{candidate['id']}/assessment", {
            "candidateId": candidate["id"], "capabilityScore": 0.9, "qualityScore": 0.9, "costScore": 0.8,
            "evidenceJson": '{"scenario":"assessment"}'
        })
        approval = cp.create_approval(s, "hiring.hire", f"Approve hiring {name}",
            targetCandidateId=candidate["id"], targetPositionId=position["id"])
        if index <= 4:
            employee, _ = cp.expect(s, f"hire-{index}", "POST", f"/v1/candidates/{candidate['id']}/hire", {"approvalId": approval["id"]})
            hired.append(employee)
        else:
            status, _, payload = cp.call("POST", f"/v1/candidates/{candidate['id']}/hire", {"approvalId": approval["id"]})
            if status != 409:
                raise ScenarioFailure(s, "starter-employee-quota", f"fifth hire unexpectedly returned HTTP {status}", status)
            blocked_candidate = candidate
    billing, _ = cp.expect(s, "billing-before-upgrade", "GET", "/v1/billing")
    employee_quota = next(item for item in billing["quotas"] if item["resource"] == "employees")
    if employee_quota["used"] != 5 or employee_quota["limit"] != 5:
        raise ScenarioFailure(s, "billing-before-upgrade", f"unexpected employee quota {employee_quota}")
    linked = create_project_with_plan(cp, s, "Auto Runner")
    status, _, _ = cp.call("POST", "/internal/tasks/claim", headers={
        "X-Runner-Id": "scenario-recruitment-runner", "X-Regent-Tenant": tenant
    })
    if status != 409:
        raise ScenarioFailure(s, "starter-auto-employee-quota", f"auto-created employee bypassed Starter quota: HTTP {status}", status)
    invoice, _ = cp.expect(s, "checkout-pro", "POST", "/v1/billing/checkout", {"planId": "pro"})
    cp.expect(s, "pay-pro", "POST", f"/v1/billing/invoices/{invoice['id']}/pay-local", {"confirmation": "PAY_LOCAL"})
    employee, _ = cp.expect(s, "hire-after-upgrade", "POST", f"/v1/candidates/{blocked_candidate['id']}/hire", {"approvalId": approval["id"]})
    auto_claim, _ = cp.expect(s, "claim-after-upgrade", "POST", "/internal/tasks/claim", headers={
        "X-Runner-Id": "scenario-recruitment-runner", "X-Regent-Tenant": tenant
    })
    billing, _ = cp.expect(s, "billing-after-upgrade", "GET", "/v1/billing")
    employee_quota = next(item for item in billing["quotas"] if item["resource"] == "employees")
    if billing["subscription"]["planId"] != "pro" or len(billing["ledger"]) != 1 or employee_quota["used"] != 7:
        raise ScenarioFailure(s, "billing-after-upgrade", "plan upgrade, append-only ledger or auto-employee quota is inconsistent")
    return {"tenantId": tenant, "employees": employee_quota["used"], "upgradedEmployee": employee["id"],
            "autoCreatedEmployeeId": auto_claim["employeeId"], "projectId": linked["projectId"]}
