using System.Text.Json;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class GovernanceService(JsonDocumentStore<GovernanceState> store,
    LocalArtifactSigner signer, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<GovernanceState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    public Task<ToolManifest> RegisterToolAsync(Guid tenantId, ToolManifest tool,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            tool.TenantId = tenantId;
            tool.CreatedAt = clock.GetUtcNow();
            tool.Status = "Draft";
            var payload = ToolPayload(tool);
            tool.Digest = LocalArtifactSigner.ComputeDigest(payload);
            tool.Signature = signer.Sign(payload);
            state.Tools.Add(tool);
            return (true, tool);
        }, cancellationToken);

    public Task<ToolAdmissionReview> AdmitToolAsync(Guid tenantId, Guid toolId, string reviewer,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var tool = Find(state.Tools, toolId);
            var payload = ToolPayload(tool);
            var signatureValid = signer.Verify(payload, tool.Signature);
            var digestValid = tool.Digest == LocalArtifactSigner.ComputeDigest(payload);
            var sbomValid = GovernancePolicy.TryGetCriticalVulnerabilities(tool.SbomJson, out var critical);
            var findings = new List<string>();
            if (!signatureValid) findings.Add("Signature verification failed.");
            if (!digestValid) findings.Add("Digest verification failed.");
            if (!sbomValid) findings.Add("SBOM is invalid.");
            if (critical > 0) findings.Add($"SBOM contains {critical} critical vulnerabilities.");
            var verdict = findings.Count == 0 && !string.IsNullOrWhiteSpace(reviewer) ? "Approved" : "Rejected";
            tool.Status = verdict == "Approved" ? "Admitted" : "Rejected";
            var review = new ToolAdmissionReview
            {
                ToolId = toolId, Reviewer = reviewer, SignatureValid = signatureValid,
                SbomValid = sbomValid, CriticalVulnerabilities = critical, Verdict = verdict, Findings = findings
            };
            state.AdmissionReviews.Add(review);
            return (true, review);
        }, cancellationToken);

    public Task<ToolManifest> RevokeToolAsync(Guid tenantId, Guid toolId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var tool = Find(state.Tools, toolId);
            tool.Status = "Revoked";
            return (true, tool);
        }, cancellationToken);

    public async Task<ToolAuthorizationResult> AuthorizeToolCallAsync(Guid tenantId,
        ToolAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var state = await GetAsync(tenantId, cancellationToken);
        var tool = state.Tools.FirstOrDefault(x => x.Name == request.ToolName && x.Version == request.Version);
        if (tool is null) return new ToolAuthorizationResult(false, "Tool version not found.", null);
        if (tool.Status != "Admitted") return new ToolAuthorizationResult(false, "Tool is not admitted.", tool.Digest);
        var payload = ToolPayload(tool);
        if (!signer.Verify(payload, tool.Signature) || tool.Digest != LocalArtifactSigner.ComputeDigest(payload))
            return new ToolAuthorizationResult(false, "Signature or digest verification failed.", tool.Digest);
        if (!tool.Capabilities.Contains(request.Capability, StringComparer.OrdinalIgnoreCase))
            return new ToolAuthorizationResult(false, "Tool does not provide the requested capability.", tool.Digest);
        if (!GrantAllows(request.TaskGrant, request.Capability))
            return new ToolAuthorizationResult(false, "Task grant does not include the requested capability.", tool.Digest);
        if (request.Taint.Equals("High", StringComparison.OrdinalIgnoreCase) && IsSensitive(request.Capability))
            return new ToolAuthorizationResult(false, "High-taint content cannot invoke sensitive capabilities.", tool.Digest);
        if (GovernancePolicy.ScanInjection(request.PayloadJson).Count > 0)
            return new ToolAuthorizationResult(false, "Potential prompt injection detected.", tool.Digest);
        return new ToolAuthorizationResult(true, "Authorized at execution boundary.", tool.Digest);
    }

    public Task<InjectionScanResult> ScanInjectionAsync(string content, CancellationToken cancellationToken)
    {
        var findings = GovernancePolicy.ScanInjection(content);
        return Task.FromResult(new InjectionScanResult
        {
            Safe = findings.Count == 0, Findings = findings,
            RecommendedAction = findings.Count == 0 ? "TreatAsUntrustedData" : "QuarantineAndReview"
        });
    }

    public Task<DlpRedactionResult> RedactAsync(Guid tenantId, string content, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var result = GovernancePolicy.Redact(content, tenantId);
            state.DlpTokens.AddRange(result.Tokens);
            return (result.Tokens.Count > 0, result);
        }, cancellationToken);

    public Task<ContentSafetyFinding?> ReviewContentAsync(Guid tenantId, string contentRef, string content,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var (category, disposition) = GovernancePolicy.ClassifyContent(content);
            if (disposition == "Allow") return (false, (ContentSafetyFinding?)null);
            var finding = new ContentSafetyFinding
            {
                TenantId = tenantId, ContentRef = contentRef, Category = category,
                Disposition = disposition, EvidenceRef = $"content:{contentRef}"
            };
            state.ContentFindings.Add(finding);
            return (true, finding);
        }, cancellationToken);

    public Task<ContentSafetyFinding> AppealAsync(Guid tenantId, ContentAppealRequest request,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var finding = Find(state.ContentFindings, request.FindingId);
            finding.Status = string.IsNullOrWhiteSpace(request.Reason) ? "InvalidAppeal" : "UnderReview";
            finding.AppealReason = request.Reason;
            finding.AppealRequester = request.Requester;
            return (true, finding);
        }, cancellationToken);

    public Task<ContentSafetyFinding> ResolveAppealAsync(Guid tenantId, Guid findingId, bool upheld,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var finding = Find(state.ContentFindings, findingId);
            if (finding.Status != "UnderReview") throw new InvalidOperationException("Finding has no pending appeal.");
            finding.Status = upheld ? "Upheld" : "Overturned";
            finding.Disposition = upheld ? finding.Disposition : "Allow";
            return (true, finding);
        }, cancellationToken);

    public Task<SecurityIncident> CreateIncidentAsync(Guid tenantId, SecurityIncident incident,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            if (!GovernancePolicy.IsValidIncidentSeverity(incident.Severity)) throw new InvalidOperationException("Invalid incident severity.");
            if (string.IsNullOrWhiteSpace(incident.Commander)) throw new InvalidOperationException("Incident commander is required.");
            incident.TenantId = tenantId;
            incident.CreatedAt = clock.GetUtcNow();
            incident.Notifications = incident.Severity is "SEV1" or "SEV2" ? ["Owner", "Operator"] : ["Owner"];
            state.Incidents.Add(incident);
            return (true, incident);
        }, cancellationToken);

    public Task<SecurityIncident> ContainIncidentAsync(Guid tenantId, Guid incidentId, string action,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var incident = Find(state.Incidents, incidentId);
            if (string.IsNullOrWhiteSpace(action)) throw new InvalidOperationException("Containment action is required.");
            incident.ContainmentActions = [.. incident.ContainmentActions, action];
            incident.Status = "Contained";
            return (true, incident);
        }, cancellationToken);

    public Task<SecurityIncident> RecoverIncidentAsync(Guid tenantId, Guid incidentId, string summary,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var incident = Find(state.Incidents, incidentId);
            if (incident.ContainmentActions.Count == 0) throw new InvalidOperationException("Containment must precede recovery.");
            incident.RecoverySummary = summary;
            incident.Status = "Recovered";
            return (true, incident);
        }, cancellationToken);

    public Task<SecurityIncident> CloseIncidentAsync(Guid tenantId, Guid incidentId, string retrospective,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var incident = Find(state.Incidents, incidentId);
            if (incident.Status != "Recovered") throw new InvalidOperationException("Incident must be recovered before closure.");
            incident.Retrospective = retrospective;
            incident.Status = "Closed";
            return (true, incident);
        }, cancellationToken);

    public Task<ApprovalDelegation> CreateApprovalDelegationAsync(Guid tenantId, ApprovalDelegation delegation,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            if (delegation.PrimaryApprover == delegation.DelegateApprover) throw new InvalidOperationException("Delegate must differ from primary approver.");
            if (delegation.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("Delegation expiry must be in the future.");
            delegation.TenantId = tenantId;
            state.ApprovalDelegations.Add(delegation);
            return (true, delegation);
        }, cancellationToken);

    public Task<string> ResolveApproverAsync(Guid tenantId, string primary, string action,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
        {
            var delegation = state.ApprovalDelegations.FirstOrDefault(x => x.Status == "Active"
                && x.ExpiresAt > clock.GetUtcNow() && x.PrimaryApprover == primary
                && (x.ActionScope == "*" || x.ActionScope == action));
            return (false, delegation?.DelegateApprover ?? primary);
        }, cancellationToken);

    public Task RevokeApprovalDelegationAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            Find(state.ApprovalDelegations, id).Status = "Revoked";
            return (true, true);
        }, cancellationToken);

    public Task RecordToolUsageAsync(Guid tenantId, ToolAuthorizationRequest request,
        ToolAuthorizationResult result, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var tool = state.Tools.FirstOrDefault(x => x.Name == request.ToolName && x.Version == request.Version);
        if (tool is null) return (false, true);
        state.ToolUsages.Add(new ToolUsageRecord
        {
            ToolId = tool.Id, Actor = request.Actor, Capability = request.Capability,
            Allowed = result.Allowed, CreatedAt = clock.GetUtcNow()
        });
        return (true, true);
    }, cancellationToken);

    public Task<ToolRevocationImpact> GetToolImpactAsync(Guid tenantId, Guid toolId,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var usages = state.ToolUsages.Where(x => x.ToolId == toolId && x.Allowed).ToArray();
        var actors = usages.Select(x => x.Actor).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return (false, new ToolRevocationImpact(actors.Length,
            usages.Select(x => x.Capability).Distinct(StringComparer.OrdinalIgnoreCase).Count(), actors));
    }, cancellationToken);

    public Task DeleteDlpTokensAsync(Guid tenantId, IReadOnlyList<string> tokens,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        state.DlpTokens.RemoveAll(x => tokens.Contains(x.Token, StringComparer.Ordinal));
        return (true, true);
    }, cancellationToken);

    public Task<ApprovalChainDto> ResolveApprovalChainAsync(Guid tenantId, string primary, string action,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var chain = new List<string> { primary };
        var delegation = state.ApprovalDelegations.FirstOrDefault(x => x.Status == "Active"
            && x.ExpiresAt > clock.GetUtcNow() && x.PrimaryApprover == primary
            && (x.ActionScope == "*" || x.ActionScope == action));
        if (delegation is not null) chain.Add(delegation.DelegateApprover);
        chain.Add("Controller");
        chain.Add("Boss");
        return (false, new ApprovalChainDto(chain.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }, cancellationToken);

    public Task<SecurityIncident> SealIncidentEvidenceAsync(Guid tenantId, Guid incidentId, string evidence,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var incident = Find(state.Incidents, incidentId);
        var hash = LocalArtifactSigner.ComputeDigest(evidence);
        var signature = signer.Sign(hash);
        incident.EvidenceRefs = [.. incident.EvidenceRefs, $"sha256:{hash}:sig:{signature}"];
        return (true, incident);
    }, cancellationToken);

    public Task<EvidenceVerificationDto> VerifyIncidentEvidenceAsync(Guid tenantId, Guid incidentId,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var incident = Find(state.Incidents, incidentId);
        var checkedCount = 0;
        foreach (var reference in incident.EvidenceRefs.Where(x => x.StartsWith("sha256:", StringComparison.Ordinal)))
        {
            var parts = reference.Split(":sig:", 2, StringSplitOptions.None);
            if (parts.Length != 2 || !signer.Verify(parts[0]["sha256:".Length..], parts[1]))
                return (false, new EvidenceVerificationDto(false, checkedCount, "Evidence signature mismatch."));
            checkedCount++;
        }
        return (false, new EvidenceVerificationDto(true, checkedCount, "Incident evidence verified."));
    }, cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId,
        Func<GovernanceState, (bool Changed, T Result)> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.GetAsync(tenantId.ToString(), cancellationToken) ?? new GovernanceState();
            if (state.TenantId == Guid.Empty) state.TenantId = tenantId;
            else if (state.TenantId != tenantId) throw new UnauthorizedAccessException("Governance state belongs to another tenant.");
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(tenantId.ToString(), state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");

    private static string ToolPayload(ToolManifest tool) => JsonSerializer.Serialize(new
    {
        tool.Id, tool.Name, tool.Version, tool.SbomJson, tool.Capabilities
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static bool GrantAllows(string grant, string capability)
    {
        if (grant == "*" || grant.Equals(capability, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            using var document = JsonDocument.Parse(grant);
            return document.RootElement.TryGetProperty("capabilities", out var capabilities)
                && capabilities.EnumerateArray().Any(x => x.GetString()?.Equals(capability, StringComparison.OrdinalIgnoreCase) == true);
        }
        catch (JsonException) { return false; }
    }

    private static bool IsSensitive(string capability) =>
        capability.StartsWith("org.", StringComparison.OrdinalIgnoreCase)
        || capability.StartsWith("key.", StringComparison.OrdinalIgnoreCase)
        || capability.StartsWith("billing.", StringComparison.OrdinalIgnoreCase)
        || capability.StartsWith("release.", StringComparison.OrdinalIgnoreCase)
        || capability.Equals("data.delete", StringComparison.OrdinalIgnoreCase);
}
