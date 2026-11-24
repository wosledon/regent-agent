import uuid
from scenario_support import ApiClient, ScenarioFailure

OPS = "http://localhost:5082"


def run():
    s = "S-10"
    ops = ApiClient(OPS, "operations")
    ops.expect(s, "ops-login", "POST", "/auth/dev-login", {})
    profile_id = f"scenario-{uuid.uuid4().hex}"
    profile, _ = ops.expect(s, "configure-backup", "PUT", "/ops/backup-profile", {
        "id": profile_id, "deploymentId": "local", "enabled": True, "components": "rustfs", "retentionDays": 35,
        "rustFs": True, "restoreDrills": True, "version": 1
    })
    jobs, _ = ops.expect(s, "run-backup", "POST", "/ops/backups/run", {"profileId": profile_id})
    if not jobs or any(job["status"] != "Completed" for job in jobs):
        raise ScenarioFailure(s, "run-backup", f"backup jobs did not complete: {jobs}")
    coverage, _ = ops.expect(s, "coverage-before-drill", "GET", f"/ops/backup-coverage/{profile_id}")
    if coverage["effectiveRpoMinutes"] is None or coverage["rtoStatus"] != "unverified":
        raise ScenarioFailure(s, "coverage-before-drill", "backup coverage did not measure RPO or falsely claimed RTO")
    restore, _ = ops.expect(s, "request-restore", "POST", "/ops/restores", {
        "backupIds": [job["id"] for job in jobs], "scope": "Full", "approvalRef": f"scenario-{uuid.uuid4().hex}",
        "requester": "SRE One"
    })
    status, _, _ = ops.call("POST", f"/ops/restores/{restore['id']}/approve", {
        "approver": "SRE One", "resolution": "Self approval"
    })
    if status != 409:
        raise ScenarioFailure(s, "self-approval", f"self-approved restore returned HTTP {status}", status)
    verified, _ = ops.expect(s, "approve-restore", "POST", f"/ops/restores/{restore['id']}/approve", {
        "approver": "SRE Two", "resolution": "Manifest hashes verified in isolated local drill"
    })
    if verified["status"] != "Verified" or verified["rtoActualMinutes"] is None:
        raise ScenarioFailure(s, "approve-restore", "two-person restore did not produce measured RTO")
    coverage, _ = ops.expect(s, "coverage-after-drill", "GET", f"/ops/backup-coverage/{profile_id}")
    if coverage["rtoStatus"] != "measured":
        raise ScenarioFailure(s, "coverage-after-drill", "RTO remained unverified after restore drill")
    return {"profileVersion": profile["version"], "backupJobIds": [job["id"] for job in jobs], "restoreId": restore["id"]}
