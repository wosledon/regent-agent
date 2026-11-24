import time
import uuid
from datetime import datetime, timezone
from scenario_support import ApiClient, ScenarioFailure

CP = "http://localhost:5080"
GW = "http://localhost:5081"


def run():
    s = "S-05"
    cp = ApiClient(CP, "controlplane")
    gw = ApiClient(GW, "gateway")
    auth = cp.register(s, "budget")
    tenant = auth["tenant_id"]
    employee = cp.start_company(s)
    token = cp.issue_agent_token(s, tenant, employee["id"], scopes=["infer:employee"])
    request_id = str(uuid.uuid4())
    status, headers, response = gw.call("POST", "/v1/chat/completions", {
        "model": "local-cheap", "messages": [{"role": "user", "content": "cross-feature budget scenario"}], "max_tokens": 32
    }, {"Authorization": f"Bearer {token}", "X-Regent-Request-Id": request_id})
    if status != 200:
        raise ScenarioFailure(s, "model-call", f"model call returned HTTP {status}", status)
    reservation = headers.get("X-Regent-Budget-Reservation-Id") or headers.get("x-regent-budget-reservation-id")
    if not reservation:
        raise ScenarioFailure(s, "reservation-header", "model response has no budget reservation id")
    period = datetime.now(timezone.utc).strftime("%Y-%m")
    ledger, _ = gw.expect(s, "gateway-ledger", "GET", f"/admin/budget/{tenant}/ledger?period={period}")
    if not any(item.get("reservationId") == reservation for item in ledger):
        raise ScenarioFailure(s, "gateway-ledger", "reservation has no settled ledger entry")
    snapshot, _ = gw.expect(s, "gateway-snapshot", "GET", f"/admin/budget/{tenant}/periods/{period}")
    if snapshot["reservedMinor"] != 0:
        raise ScenarioFailure(s, "gateway-snapshot", "completed call did not release reservation")
    reconciled, _ = gw.expect(s, "reconcile", "POST", f"/admin/budget/{tenant}/reconcile", {
        "period": period, "provider": "openai", "currency": "CNY", "providerAmountMinor": 0,
        "invoiceRef": f"scenario-{uuid.uuid4().hex}", "reason": "Local deterministic catalog", "thresholdBasisPoints": 0,
        "thresholdAbsoluteMinor": 0
    })
    if reconciled["status"] != "Matched":
        raise ScenarioFailure(s, "reconcile", f"unexpected reconciliation status {reconciled['status']}")
    projected = False
    for _ in range(20):
        policy, _ = cp.expect(s, "controlplane-cost-projection", "GET", "/v1/runtime-policy")
        projected = any(item.get("reason") == request_id for item in policy["costs"])
        if projected:
            break
        time.sleep(1)
    if not projected:
        raise ScenarioFailure(s, "controlplane-cost-projection", "Gateway usage was not projected into ControlPlane cost ledger")
    return {"tenantId": tenant, "reservationId": reservation, "requestId": request_id}
