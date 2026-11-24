from scenario_support import ApiClient, ScenarioFailure

CP = "http://localhost:5080"


def run():
    s = "S-06"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "lifecycle")
    tenant = auth["tenant_id"]
    employee = cp.start_company(s)
    token = cp.issue_agent_token(s, tenant, employee["id"])
    deleted, _ = cp.expect(s, "soft-delete", "POST", "/v1/lifecycle/deletions", {
        "resourceType": "Tenant", "resourceId": tenant, "reason": "Scenario soft-delete boundary"
    })
    status, _, _ = cp.call("GET", "/v1/dashboard/summary")
    if status not in (401, 403):
        raise ScenarioFailure(s, "human-fail-closed", f"soft-deleted tenant dashboard returned HTTP {status}", status)
    status, _, _ = cp.call("GET", "/agent/v1/me", headers={"Authorization": f"Bearer {token}"})
    if status not in (401, 403):
        raise ScenarioFailure(s, "agent-fail-closed", f"soft-deleted tenant Agent API returned HTTP {status}", status)
    cp.expect(s, "restore", "POST", f"/v1/lifecycle/deletions/{deleted['id']}/restore")
    cp.expect(s, "dashboard-after-restore", "GET", "/v1/dashboard/summary")
    return {"tenantId": tenant, "deletionRequestId": deleted["id"]}
