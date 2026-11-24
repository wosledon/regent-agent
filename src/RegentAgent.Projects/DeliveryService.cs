using RegentAgent.Contracts.Projects;
using RegentAgent.Storage;

namespace RegentAgent.Projects;

public sealed class DeliveryService(JsonDocumentStore<ProjectDeliveryState> store, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<ProjectDeliveryState> GetAsync(Guid tenantId, Guid projectId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, projectId, state => (false, state), cancellationToken);

    public Task<ResearchFinding> AddFindingAsync(Guid tenantId, Guid projectId, ResearchFinding finding,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            RequireValid(DeliveryPolicy.ValidateFinding(finding));
            finding.TenantId = tenantId;
            finding.ProjectId = projectId;
            state.Findings.Add(finding);
            return (true, finding);
        }, cancellationToken);

    public Task<ProductBrief> CreateBriefAsync(Guid tenantId, Guid projectId, ProductBrief brief,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            if (brief.FindingIds.Any(id => state.Findings.All(x => x.Id != id)))
                throw new InvalidOperationException("Brief references unknown findings.");
            if (brief.MvpScope.Count == 0 || brief.SuccessMetrics.Count == 0)
                throw new InvalidOperationException("MVP scope and measurable success metrics are required.");
            brief.TenantId = tenantId;
            brief.ProjectId = projectId;
            state.Briefs.Add(brief);
            return (true, brief);
        }, cancellationToken);

    public Task<GoNoGoDecision> DecideAsync(Guid tenantId, Guid projectId, Guid briefId, string decision,
        string rationale, IReadOnlyList<Guid> evidenceIds, int reround, string decider,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            if (state.Briefs.All(x => x.Id != briefId)) throw new KeyNotFoundException("Brief not found.");
            if (decision is not ("Go" or "NoGo")) throw new InvalidOperationException("Decision must be Go or NoGo.");
            if (string.IsNullOrWhiteSpace(rationale) || string.IsNullOrWhiteSpace(decider)) throw new InvalidOperationException("Decision rationale and decider are required.");
            if (reround is < 0 or > 2) throw new InvalidOperationException("Reround must be between 0 and 2.");
            if (evidenceIds.Count == 0 || evidenceIds.Any(id => state.Findings.All(x => x.Id != id)))
                throw new InvalidOperationException("Decision evidence must reference existing findings.");
            var record = new GoNoGoDecision
            {
                TenantId = tenantId, ProjectId = projectId, BriefId = briefId, Decision = decision,
                Rationale = rationale, EvidenceIds = evidenceIds, Reround = reround,
                Forced = reround >= 2, Decider = decider
            };
            state.Decisions.Add(record);
            return (true, record);
        }, cancellationToken);

    public Task<DeliveryRequirement> CreateRequirementAsync(Guid tenantId, Guid projectId,
        DeliveryRequirement requirement, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            if (state.Decisions.All(x => x.Decision != "Go")) throw new InvalidOperationException("A Go decision is required before development requirements.");
            requirement.TenantId = tenantId;
            requirement.ProjectId = projectId;
            state.Requirements.Add(requirement);
            return (true, requirement);
        }, cancellationToken);

    public Task<DeliveryRequirement> ReadyRequirementAsync(Guid tenantId, Guid projectId, Guid requirementId,
        int totalWeight, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var requirement = Find(state.Requirements, tenantId, requirementId);
            if (totalWeight <= 0) throw new InvalidOperationException("Total requirement weight must be positive.");
            requirement.TotalWeight = totalWeight;
            RequireValid(DeliveryPolicy.ValidateRequirementReady(requirement));
            requirement.Status = "Ready";
            return (true, requirement);
        }, cancellationToken);

    public Task<DecompositionPlan> ProposePlanAsync(Guid tenantId, Guid projectId, DecompositionPlan plan,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var requirement = Find(state.Requirements, tenantId, plan.RequirementId);
            var errors = DeliveryPolicy.ValidatePlan(requirement, plan);
            plan.ValidationErrors = errors;
            plan.Status = errors.Count == 0 ? "Proposed" : "Invalid";
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            state.Plans.Add(plan);
            return (true, plan);
        }, cancellationToken);

    public Task<DecompositionPlan> ApprovePlanAsync(Guid tenantId, Guid projectId, Guid planId,
        CancellationToken cancellationToken) => ApprovePlanAsync(tenantId, projectId, planId, "Controller", null, cancellationToken);

    public Task<DecompositionPlan> ApprovePlanAsync(Guid tenantId, Guid projectId, Guid planId,
        string approver, string? approvalId, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var plan = state.Plans.SingleOrDefault(x => x.Id == planId) ?? throw new KeyNotFoundException("Plan not found.");
            if (plan.Status != "Proposed") throw new InvalidOperationException("Only proposed plans can be approved.");
            if (string.IsNullOrWhiteSpace(approver)) throw new InvalidOperationException("Plan approver is required.");
            if (plan.RiskLevel == "High" && string.IsNullOrWhiteSpace(approvalId))
                throw new InvalidOperationException("High-risk plans require an approval reference.");
            plan.Status = "Approved";
            plan.ApprovedBy = approver;
            plan.ApprovalId = approvalId;
            foreach (var task in plan.Tasks)
            {
                task.TenantId = tenantId;
                task.ProjectId = projectId;
                task.RequirementId = plan.RequirementId;
                task.Status = "Ready";
                state.Tasks.Add(task);
            }
            return (true, plan);
        }, cancellationToken);

    public Task<DeliveryTask> ClaimTaskAsync(Guid tenantId, Guid projectId, Guid taskId, string assignee,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var task = Find(state.Tasks, tenantId, taskId);
            if (task.Status != "Ready") throw new InvalidOperationException("Task is not ready for claim.");
            task.Assignee = assignee;
            task.Status = "Claimed";
            return (true, task);
        }, cancellationToken);

    public Task<DeliveryTask> SubmitTaskAsync(Guid tenantId, Guid projectId, Guid taskId, string author,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var task = Find(state.Tasks, tenantId, taskId);
            if (task.Assignee != author) throw new InvalidOperationException("Only the assigned employee can submit.");
            task.Status = "InReview";
            return (true, task);
        }, cancellationToken);

    public Task<DoneEvidence> CompleteTaskAsync(Guid tenantId, Guid projectId, Guid taskId,
        DoneEvidence evidence, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var task = Find(state.Tasks, tenantId, taskId);
            if (task.Status != "InReview") throw new InvalidOperationException("Task must be in review.");
            evidence.WorkItemId = taskId;
            evidence.WorkItemType = "Task";
            evidence.ValidatedAt = clock.GetUtcNow();
            RequireValid(DeliveryPolicy.ValidateTaskDone(task, evidence, state.Reviews, state.Defects));
            task.Status = "Done";
            task.Validator = evidence.Validator;
            state.Evidence.Add(evidence);
            return (true, evidence);
        }, cancellationToken);

    public Task<Defect> ReportDefectAsync(Guid tenantId, Guid projectId, Defect defect,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            if (string.IsNullOrWhiteSpace(defect.ReproductionSteps)) throw new InvalidOperationException("Reproduction steps are required.");
            defect.TenantId = tenantId;
            defect.ProjectId = projectId;
            state.Defects.Add(defect);
            return (true, defect);
        }, cancellationToken);

    public Task<Defect> FixDefectAsync(Guid tenantId, Guid projectId, Guid defectId, string fixer,
        EvidenceRef evidence, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
    {
        var defect = Find(state.Defects, tenantId, defectId);
        if (defect.Status is not ("New" or "Reopened")) throw new InvalidOperationException("Defect is not awaiting a fix.");
        if (string.IsNullOrWhiteSpace(fixer) || string.IsNullOrWhiteSpace(evidence.Id) || string.IsNullOrWhiteSpace(evidence.Location))
            throw new InvalidOperationException("Fixer and fix evidence are required.");
        defect.Fixer = fixer;
        defect.FixEvidence = [evidence];
        defect.Status = "ReadyForRetest";
        return (true, defect);
    }, cancellationToken);

    public Task<Defect> RetestDefectAsync(Guid tenantId, Guid projectId, Guid defectId, bool passed,
        string retester, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var defect = Find(state.Defects, tenantId, defectId);
            if (defect.Status != "ReadyForRetest") throw new InvalidOperationException("Defect is not ready for retest.");
            if (defect.RetestRound >= 2) throw new InvalidOperationException("Defect retest limit of two rounds reached.");
            if (string.IsNullOrWhiteSpace(retester) || retester == defect.Fixer)
                throw new InvalidOperationException("Fixer cannot close their own defect.");
            defect.Retester = retester;
            defect.RetestRound++;
            defect.Status = passed ? "Closed" : "Reopened";
            return (true, defect);
        }, cancellationToken);

    public Task<Review> AddReviewAsync(Guid tenantId, Guid projectId, Review review,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            if (review.Reviewer == review.Author || string.IsNullOrWhiteSpace(review.Author))
                throw new InvalidOperationException("Reviewer and author must be different.");
            if (review.Verdict is not ("Approved" or "Rejected")) throw new InvalidOperationException("Verdict must be Approved or Rejected.");
            if (review.SubjectType == "Task" && state.Tasks.FirstOrDefault(x => x.Id == review.SubjectId) is { } task
                && (review.Reviewer == task.Assignee || review.Reviewer == task.Author))
                throw new InvalidOperationException("Task executor or author cannot independently review the task.");
            review.TenantId = tenantId;
            review.ProjectId = projectId;
            state.Reviews.Add(review);
            return (true, review);
        }, cancellationToken);

    public Task<AcceptanceReport> AddAcceptanceReportAsync(Guid tenantId, Guid projectId,
        AcceptanceReport report, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var requirement = Find(state.Requirements, tenantId, report.RequirementId);
            if (string.IsNullOrWhiteSpace(report.Reviewer) || report.Evidence.Count == 0)
                throw new InvalidOperationException("Acceptance reviewer and evidence are required.");
            if (new[] { report.FunctionalScore, report.UsabilityScore, report.DesignScore, report.AccessibilityScore }.Any(score => score is < 0 or > 100))
                throw new InvalidOperationException("Acceptance scores must be between 0 and 100.");
            if (report.AuthorEmployeeId == report.Reviewer) throw new InvalidOperationException("Author cannot perform independent acceptance.");
            var tasks = state.Tasks.Where(x => x.RequirementId == requirement.Id).ToArray();
            if (tasks.Any(x => x.Assignee == report.Reviewer || x.Author == report.Reviewer))
                throw new InvalidOperationException("Task executor or author cannot perform independent acceptance.");
            report.TenantId = tenantId;
            report.ProjectId = projectId;
            state.AcceptanceReports.Add(report);
            var reports = state.AcceptanceReports.Where(x => x.RequirementId == requirement.Id)
                .GroupBy(x => x.Reviewer, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).ToArray();
            if (reports.Length is >= 2 and <= 3 && reports.All(x => x.Passed)
                && tasks.Length > 0 && tasks.All(x => x.Status == "Done")) requirement.Status = "Accepted";
            return (true, report);
        }, cancellationToken);

    public Task<ReleaseRecord> ReleaseAsync(Guid tenantId, Guid projectId, string version, string artifactRef,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            RequireValid(DeliveryPolicy.ValidateRelease(state, projectId, artifactRef));
            var record = new ReleaseRecord
            {
                TenantId = tenantId, ProjectId = projectId, Version = version,
                ArtifactRef = artifactRef, ReleasedAt = clock.GetUtcNow()
            };
            state.Releases.Add(record);
            return (true, record);
        }, cancellationToken);

    public Task<ReleaseRecord> ObserveReleaseAsync(Guid tenantId, Guid projectId, Guid releaseId,
        bool healthy, string metricsJson, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
    {
        var release = Find(state.Releases, tenantId, releaseId);
        release.ObservationStatus = healthy ? "Healthy" : "Degraded";
        release.MetricsJson = metricsJson;
        release.Status = healthy ? "Released" : "RollbackRecommended";
        return (true, release);
    }, cancellationToken);

    public Task<Retrospective> TerminateProjectAsync(Guid tenantId, Guid projectId, Retrospective retrospective,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
    {
        if (string.IsNullOrWhiteSpace(retrospective.Outcome) || retrospective.Lessons.Count == 0)
            throw new InvalidOperationException("Termination requires outcome and lessons.");
        retrospective.TenantId = tenantId;
        retrospective.ProjectId = projectId;
        state.ProjectStatus = "Cancelled";
        state.Retrospectives.Add(retrospective);
        return (true, retrospective);
    }, cancellationToken);

    public Task<ReleaseRecord> RollbackAsync(Guid tenantId, Guid projectId, Guid releaseId,
        string targetVersion, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            var release = Find(state.Releases, tenantId, releaseId);
            if (state.Releases.All(x => x.Version != targetVersion)) throw new KeyNotFoundException("Rollback target not found.");
            release.Status = "RolledBack";
            release.RolledBackTo = targetVersion;
            var rollback = new ReleaseRecord
            {
                TenantId = tenantId, ProjectId = projectId, Version = targetVersion,
                ArtifactRef = state.Releases.Single(x => x.Version == targetVersion).ArtifactRef,
                Status = "RolledBackRestore", ReleasedAt = clock.GetUtcNow(), RolledBackTo = targetVersion
            };
            state.Releases.Add(rollback);
            return (true, rollback);
        }, cancellationToken);

    public Task<Retrospective> AddRetrospectiveAsync(Guid tenantId, Guid projectId,
        Retrospective retrospective, CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
        {
            retrospective.TenantId = tenantId;
            retrospective.ProjectId = projectId;
            state.Retrospectives.Add(retrospective);
            return (true, retrospective);
        }, cancellationToken);

    public Task MarkRuntimeClaimedAsync(Guid tenantId, Guid projectId, Guid taskId, string assignee,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
    {
        var task = Find(state.Tasks, tenantId, taskId);
        if (task.Status is "Ready" or "Backlog")
        {
            task.Assignee = assignee;
            task.Status = "Claimed";
        }
        return (true, task);
    }, cancellationToken);

    public Task MarkRuntimeCompletedAsync(Guid tenantId, Guid projectId, Guid taskId, string assignee,
        CancellationToken cancellationToken) => MutateAsync(tenantId, projectId, state =>
    {
        var task = Find(state.Tasks, tenantId, taskId);
        if (task.Status is "Ready" or "Backlog" or "Claimed")
        {
            task.Assignee = assignee;
            task.Status = "ReadyForReview";
        }
        return (true, task);
    }, cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId, Guid projectId,
        Func<ProjectDeliveryState, (bool Changed, T Result)> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await store.GetAsync(projectId.ToString(), cancellationToken) ?? new ProjectDeliveryState();
            if (state.TenantId == Guid.Empty)
            {
                state.TenantId = tenantId;
                state.ProjectId = projectId;
            }
            else if (state.TenantId != tenantId || state.ProjectId != projectId)
            {
                throw new UnauthorizedAccessException("Project delivery state belongs to another tenant.");
            }
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(projectId.ToString(), state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static T Find<T>(IEnumerable<T> items, Guid tenantId, Guid id) where T : class
    {
        return items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("TenantId")!.GetValue(x) == tenantId
            && (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
            ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");
    }

    private static void RequireValid(IReadOnlyList<string> errors)
    {
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
    }
}
