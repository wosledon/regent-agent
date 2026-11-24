from scenario_support import ApiClient, ScenarioFailure

CP = "http://localhost:5080"


def run():
    s = "S-08"
    cp = ApiClient(CP, "controlplane")
    auth = cp.register(s, "oidc")
    tenant = auth["tenant_id"]
    cp.start_company(s)
    cp.expect(s, "configure-oidc", "PUT", "/v1/tenants/{}/identity-providers/Oidc".format(tenant), {
        "tenantId": tenant, "provider": "Oidc", "issuer": "https://idp.example.test/tenant", "clientId": "scenario-client",
        "enabled": True, "status": "Active", "groupMappingJson": "{}", "scopes": "openid profile", "groupClaim": "groups"
    }, headers={"If-Match": "*"})
    emergency, _ = cp.expect(s, "configure-emergency", "POST", f"/v1/tenants/{tenant}/emergency-access")
    policy, _ = cp.expect(s, "required-policy", "PUT", f"/v1/tenants/{tenant}/authentication-policy", {
        "mode": "required", "requireGroupClaim": False, "allowedAcrValues": [], "requiredAmrValues": [],
        "maxAuthenticationAgeMinutes": 60, "maxSessionMinutes": 480, "idleTimeoutMinutes": 30, "version": 1
    }, headers={"If-Match": "\"1\""})
    if policy["mode"] != "required":
        raise ScenarioFailure(s, "required-policy", "required SSO policy was not activated")
    password = ApiClient(CP, "password-client")
    status, _, _ = password.call("POST", "/auth/login", {"email": auth["email"], "password": "strong-password-123"})
    if status != 403:
        raise ScenarioFailure(s, "password-blocked", f"required tenant password login returned HTTP {status}", status)
    status, _, _ = password.call("POST", "/auth/github/login", {
        "tenantId": tenant, "issuer": "https://github.com", "subject": "scenario", "email": auth["email"], "displayName": "Owner"
    })
    if status != 403:
        raise ScenarioFailure(s, "github-blocked", f"required tenant GitHub login returned HTTP {status}", status)
    emergency_client = ApiClient(CP, "emergency-client")
    used, _ = emergency_client.expect(s, "use-emergency", "POST", "/auth/emergency-access", {
        "tenantId": tenant, "credential": emergency["credential"], "purpose": "Scenario restore unavailable IdP"
    })
    if used["sessionKind"] != "Emergency" or used["rotatedCredential"] == emergency["credential"]:
        raise ScenarioFailure(s, "use-emergency", "emergency credential did not rotate or session is not restricted")
    status, _, _ = emergency_client.call("GET", "/v1/tenants")
    if status != 403:
        raise ScenarioFailure(s, "ordinary-api-blocked", f"emergency session ordinary API returned HTTP {status}", status)
    emergency_client.expect(s, "sso-restore-api", "GET", f"/v1/tenants/{tenant}/authentication-policy")
    sessions, _ = emergency_client.expect(s, "emergency-session-list", "GET", "/auth/sessions")
    if not any(item["id"] == used["sessionId"] and item["sessionKind"] == "Emergency" for item in sessions):
        raise ScenarioFailure(s, "emergency-session-list", "emergency session is missing")
    return {"tenantId": tenant, "sessionId": used["sessionId"]}
