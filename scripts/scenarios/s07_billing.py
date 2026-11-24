import uuid
from scenario_support import ApiClient, ScenarioFailure

CP = "http://localhost:5080"


def run():
    s = "S-07"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "billing")
    tenant = auth["tenant_id"]
    cp.start_company(s)
    for index in range(3):
        cp.expect(s, f"starter-project-{index}", "POST", "/v1/projects", {
            "name": f"Starter project {index}", "owner": "Controller", "mission": "Use Starter quota",
            "targetDays": 5, "budgetMinor": 1000, "currency": "CNY"
        })
    status, _, _ = cp.call("POST", "/v1/projects", {
        "name": "Blocked Starter project", "owner": "Controller", "mission": "Exceed quota",
        "targetDays": 5, "budgetMinor": 1000, "currency": "CNY"
    })
    if status != 409:
        raise ScenarioFailure(s, "starter-project-quota", f"fourth Starter project returned HTTP {status}", status)
    invoice, _ = cp.expect(s, "checkout", "POST", "/v1/billing/checkout", {"planId": "pro"})
    cp.expect(s, "pay", "POST", f"/v1/billing/invoices/{invoice['id']}/pay-local", {"confirmation": "PAY_LOCAL"})
    cp.expect(s, "pay-replay", "POST", f"/v1/billing/invoices/{invoice['id']}/pay-local", {"confirmation": "PAY_LOCAL"})
    cp.expect(s, "pro-project", "POST", "/v1/projects", {
        "name": "Pro project", "owner": "Controller", "mission": "Use upgraded quota",
        "targetDays": 5, "budgetMinor": 1000, "currency": "CNY"
    })
    billing, _ = cp.expect(s, "billing-summary", "GET", "/v1/billing")
    if billing["subscription"]["planId"] != "pro" or len(billing["ledger"]) != 1:
        raise ScenarioFailure(s, "billing-summary", "local payment replay duplicated ledger or failed to upgrade plan")
    return {"tenantId": tenant, "invoiceId": invoice["id"], "projects": 4}
