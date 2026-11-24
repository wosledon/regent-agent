import uuid
from scenario_support import ApiClient, ScenarioFailure

CP = "http://localhost:5080"
OPS = "http://localhost:5082"


def run():
    s = "S-09"
    ops = ApiClient(OPS, "operations")
    ops.expect(s, "ops-login", "POST", "/auth/dev-login", {})
    metric = f"scenario.api.{uuid.uuid4().hex}"
    slo_name = f"scenario-{uuid.uuid4().hex}"
    ops.expect(s, "save-slo", "POST", "/ops/observability/slos", {
        "name": slo_name, "metric": metric, "target": 0.99, "windowDays": 1
    })
    ops.expect(s, "failed-samples", "POST", "/telemetry/browser", {
        "serviceName": "tenant-dashboard", "samples": [
            {"metric": metric, "value": 500, "success": False, "traceId": str(uuid.uuid4())} for _ in range(5)
        ]
    }, status=202)
    alerts, _ = ops.expect(s, "alerts", "GET", "/ops/observability/alerts")
    alert = next((item for item in alerts if item["sloName"] == slo_name and item["status"] != "Resolved"), None)
    if alert is None:
        raise ScenarioFailure(s, "alerts", "SLO failure did not create an alert")
    cp = ApiClient(CP, "controlplane")
    cp.expect(s, "owner-login", "POST", "/auth/dev-login", {"email": "owner@example.test"})
    dashboard, _ = cp.expect(s, "action-center", "GET", "/v1/dashboard/summary")
    if not any(item["sourceType"] == "Observability" and item["id"] == alert["id"] for item in dashboard["actions"]):
        raise ScenarioFailure(s, "action-center", "Ops alert is not projected into tenant action center")
    ops.expect(s, "acknowledge", "POST", f"/ops/observability/alerts/{alert['id']}/acknowledge", {"operator": "Scenario SRE"}, status=204)
    alerts, _ = ops.expect(s, "alerts-after-ack", "GET", "/ops/observability/alerts")
    if next(item for item in alerts if item["id"] == alert["id"])["status"] != "Acknowledged":
        raise ScenarioFailure(s, "alerts-after-ack", "alert acknowledgement was not persisted")
    ops.expect(s, "resolve", "POST", f"/ops/observability/alerts/{alert['id']}/resolve", headers={"X-Resolution": "Scenario recovered"}, status=204)
    dashboard, _ = cp.expect(s, "action-center-after-resolve", "GET", "/v1/dashboard/summary")
    if any(item["sourceType"] == "Observability" and item["id"] == alert["id"] for item in dashboard["actions"]):
        raise ScenarioFailure(s, "action-center-after-resolve", "resolved alert remains in action center")
    return {"alertId": alert["id"], "metric": metric}
