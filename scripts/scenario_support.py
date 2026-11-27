"""Small stdlib-only client shared by local cross-feature scenario trials."""
import http.cookiejar
import json
import urllib.error
import urllib.request
import uuid
import zipfile
import io


class ScenarioFailure(AssertionError):
    def __init__(self, scenario, step, message, status=None, code=None):
        super().__init__(f"{scenario}/{step}: {message}" + (f" [HTTP {status} {code}]" if status else ""))
        self.scenario = scenario
        self.step = step
        self.status = status
        self.code = code


class ApiClient:
    def __init__(self, base_url, name="client"):
        self.base_url = base_url.rstrip("/")
        self.name = name
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))

    def call(self, method, path, body=None, headers=None, raw=False):
        request_headers = {"Content-Type": "application/json", **(headers or {})}
        if not request_headers.get("X-Request-Id"):
            request_headers["X-Request-Id"] = str(uuid.uuid4())
        if (path.startswith("/v1") or path.startswith("/agent/v1")) and method.upper() in {"POST", "PUT", "PATCH", "DELETE"}:
            request_headers.setdefault("Idempotency-Key", str(uuid.uuid4()))
        data = None if body is None else json.dumps(body).encode("utf-8")
        request = urllib.request.Request(self.base_url + path, data=data, method=method.upper(), headers=request_headers)
        try:
            with self.opener.open(request, timeout=30) as response:
                payload = response.read()
                return response.status, dict(response.headers), payload if raw else json.loads(payload or b"null")
        except urllib.error.HTTPError as error:
            payload = error.read()
            try:
                parsed = json.loads(payload or b"null")
            except json.JSONDecodeError:
                parsed = None
            return error.code, dict(error.headers), payload if raw else parsed

    def expect(self, scenario, step, method, path, body=None, status=200, headers=None):
        actual, response_headers, payload = self.call(method, path, body, headers)
        if actual != status:
            code = payload.get("error", {}).get("code") if isinstance(payload, dict) else None
            raise ScenarioFailure(scenario, step, f"expected HTTP {status}, got {actual}", actual, code)
        return payload, response_headers

    def register(self, scenario, label):
        email = f"{label}-{uuid.uuid4().hex}@example.test"
        auth, _ = self.expect(scenario, "register", "POST", "/auth/register", {
            "email": email, "password": "strong-password-123", "displayName": "Scenario Owner", "companyName": "Scenario Co"
        })
        self.tenant_id = auth["tenant_id"]
        self.user_id = auth["user_id"]
        return auth

    def start_company(self, scenario):
        employee, _ = self.expect(scenario, "start-company", "POST", "/v1/company/start")
        return employee

    def independent_approver(self, scenario):
        cached = getattr(self, "_independent_approver", None)
        if cached is not None:
            return cached
        email = f"approver-{uuid.uuid4().hex}@example.test"
        approver = ApiClient(self.base_url, f"{self.name}-approver")
        approver.register(scenario, "approver")
        invitation, _ = self.expect(scenario, "invite-independent-approver", "POST",
            f"/v1/tenants/{self.tenant_id}/invitations", {
                "email": email, "role": "CoOwner", "expiresInHours": 1
            })
        approver.expect(scenario, "accept-independent-approver", "POST", "/auth/invitations/accept",
            {"token": invitation["token"]})
        approver.expect(scenario, "switch-independent-approver", "POST", "/auth/switch-tenant",
            {"tenantId": self.tenant_id})
        self._independent_approver = approver
        return approver

    def create_approval(self, scenario, action_type, title, targetEmployeeId=None,
                        targetCandidateId=None, targetPositionId=None, approver=None):
        approver = approver or self.independent_approver(scenario)
        payload = {
            "actionType": action_type, "title": title, "impact": "Cross-feature scenario approval",
            "severity": "high", "requester": "Scenario Owner", "expiresInMinutes": 60,
            "evidenceRefs": ["scenario:evidence"]
        }
        if targetEmployeeId is not None:
            payload["targetEmployeeId"] = targetEmployeeId
        if targetCandidateId is not None:
            payload["targetCandidateId"] = targetCandidateId
        if targetPositionId is not None:
            payload["targetPositionId"] = targetPositionId
        approval, _ = self.expect(scenario, f"approval-create:{action_type}", "POST", "/v1/approvals", payload)
        decided, _ = approver.expect(scenario, f"approval-decide:{action_type}",
            "POST", f"/v1/approvals/{approval['id']}/decision", {
                "decision": "approve", "reason": "Scenario evidence verified"
            })
        return decided

    def issue_agent_token(self, scenario, tenant_id, employee_id, task_id=None, scopes=None):
        body = {"tenantId": tenant_id, "employeeId": employee_id}
        if task_id:
            body["taskId"] = task_id
        if scopes:
            body["scopes"] = scopes
        issued, _ = self.expect(scenario, "issue-agent-token", "POST", "/auth/dev-agent-token", body)
        return issued["token"]

    def agent_headers(self, token, key=None):
        return {"Authorization": f"Bearer {token}", "Idempotency-Key": key or str(uuid.uuid4())}


def hire_employee(client, scenario, name, position_name=None):
    position_name = position_name or f"Position {name}"
    position, _ = client.expect(scenario, f"position:{name}", "POST", "/v1/positions", {
        "id": str(uuid.uuid4()), "name": position_name, "responsibilities": "Execute scenario work",
        "capabilities": ["code.implement"], "budgetMinor": 0, "parentPositionId": None, "status": "Open", "version": 1
    })
    candidate, _ = client.expect(scenario, f"candidate:{name}", "POST", "/v1/candidates", {
        "positionId": position["id"], "displayName": name, "modelVersion": "local-cheap-v1",
        "jobDescription": "Execute and verify deterministic delivery."
    })
    client.expect(scenario, f"assess:{name}", "POST", f"/v1/candidates/{candidate['id']}/assessment", {
        "candidateId": candidate["id"], "capabilityScore": 0.9, "qualityScore": 0.9, "costScore": 0.8,
        "evidenceJson": '{"scenario":"assessment"}'
    })
    approval = client.create_approval(scenario, "hiring.hire", f"Approve hiring {name}",
        targetCandidateId=candidate["id"], targetPositionId=position["id"])
    employee, _ = client.expect(scenario, f"hire:{name}", "POST", f"/v1/candidates/{candidate['id']}/hire", {"approvalId": approval["id"]})
    return employee, position, candidate, approval


def create_project_with_plan(client, scenario, assignee="Controller"):
    project, _ = client.expect(scenario, "create-project", "POST", "/v1/projects", {
        "name": f"Scenario project {uuid.uuid4().hex[:8]}", "owner": assignee,
        "mission": "Verify cross-feature delivery linkage", "targetDays": 7, "budgetMinor": 100_000, "currency": "CNY"
    })
    project_id = project["project"]["id"]
    finding, _ = client.expect(scenario, "create-finding", "POST", f"/v1/projects/{project_id}/delivery/findings", {
        "claim": "A deterministic cross-feature path is required",
        "sources": [{"id": f"source-{uuid.uuid4().hex}", "kind": "interview", "location": "local:scenario"}],
        "keyClaim": False
    })
    brief, _ = client.expect(scenario, "create-brief", "POST", f"/v1/projects/{project_id}/delivery/briefs", {
        "title": "Cross-feature brief", "problem": "Modules are not linked", "mvpScope": ["Linked delivery"],
        "successMetrics": {"linked_tasks": "3"}, "findingIds": [finding["id"]]
    })
    client.expect(scenario, "go-decision", "POST", f"/v1/projects/{project_id}/delivery/briefs/{brief['id']}/decision", {
        "decision": "Go", "rationale": "Scenario evidence supports implementation", "evidenceIds": [finding["id"]],
        "reround": 0, "decider": "Controller"
    })
    requirement, _ = client.expect(scenario, "create-requirement", "POST", f"/v1/projects/{project_id}/delivery/requirements", {
        "title": "Linked runtime requirement", "problem": "Tasks must reach runtime", "scope": "Create three linked tasks",
        "priority": "P1", "acceptanceCriteria": ["Tasks are claimable"]
    })
    client.expect(scenario, "ready-requirement", "POST", f"/v1/projects/{project_id}/delivery/requirements/{requirement['id']}/ready", {"totalWeight": 6})
    tasks = [{
        "id": str(uuid.uuid4()), "title": f"Linked task {index}", "taskType": "engineering.implement",
        "requiredCapabilitiesJson": '["code.implement"]', "dependsOn": [], "weight": 2,
        "assignee": assignee, "acceptanceCriteria": ["Tasks are claimable"]
    } for index in range(1, 4)]
    plan, _ = client.expect(scenario, "propose-plan", "POST", f"/v1/projects/{project_id}/delivery/plans", {
        "requirementId": requirement["id"], "requirementVersion": requirement["version"], "riskLevel": "Low", "tasks": tasks
    })
    approved, _ = client.expect(scenario, "approve-plan", "POST", f"/v1/projects/{project_id}/delivery/plans/{plan['id']}/approve", {"approver": "Controller"})
    return {"projectId": project_id, "requirementId": requirement["id"], "taskIds": [task["id"] for task in tasks], "plan": approved}


def error_code(payload):
    return payload.get("error", {}).get("code") if isinstance(payload, dict) else None
