using RegentAgent.Contracts.Projects;
using RegentAgent.Storage;

namespace RegentAgent.Projects;

public sealed class TaskGovernanceService(JsonDocumentStore<TaskGovernanceState> store, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<TaskTypeDefinition> RegisterTaskTypeAsync(Guid tenantId, TaskTypeDefinition definition,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        state.TaskTypes.RemoveAll(x => x.TaskType == definition.TaskType);
        state.TaskTypes.Add(definition);
        return (true, definition);
    }, cancellationToken);

    public Task<CapabilityAssessment> AssessAsync(Guid tenantId, CapabilityAssessment assessment,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        assessment.EffectiveScore = assessment.ModelScore * 0.5m + assessment.HistoryScore * 0.3m
            + assessment.ToolFit * 0.1m + assessment.ContextFit * 0.1m;
        state.Capabilities.RemoveAll(x => x.EmployeeId == assessment.EmployeeId && x.Dimension == assessment.Dimension);
        state.Capabilities.Add(assessment);
        return (true, assessment);
    }, cancellationToken);

    public Task<ClaimScoreDto> ScoreClaimAsync(Guid tenantId, Guid employeeId, string taskType, int currentWip,
        int wipLimit, decimal historySuccess, decimal toolFit, decimal costEfficiency, bool independent,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var definition = state.TaskTypes.FirstOrDefault(x => x.TaskType == taskType)
            ?? throw new KeyNotFoundException("Task type not found.");
        var capability = state.Capabilities.Where(x => x.EmployeeId == employeeId)
            .Average(x => (decimal?)x.EffectiveScore) ?? 0m;
        var (score, eligible, reason) = CalculateClaimScore(capability, currentWip, wipLimit,
            historySuccess, toolFit, costEfficiency, independent);
        return (false, new ClaimScoreDto(employeeId, score, eligible, reason));
    }, cancellationToken);

    public Task<AutoAssignResult> AutoAssignAsync(Guid tenantId, AutoAssignRequest request,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (state.TaskTypes.All(x => x.TaskType != request.TaskType)) throw new KeyNotFoundException("Task type not found.");
        var existing = state.Assignments.LastOrDefault(x => x.TaskId == request.TaskId && x.Status == "Assigned");
        if (existing is not null)
            return (false, new AutoAssignResult(true, existing.EmployeeId, existing.Score, "Task already has an active assignment."));
        var candidates = request.Candidates.Select(candidate =>
        {
            var capability = state.Capabilities.Where(x => x.EmployeeId == candidate.EmployeeId)
                .Average(x => (decimal?)x.EffectiveScore) ?? 0m;
            var (score, eligible, reason) = CalculateClaimScore(capability, candidate.CurrentWip, candidate.WipLimit,
                candidate.HistorySuccess, candidate.ToolFit, candidate.CostEfficiency, candidate.Independent);
            return (Candidate: candidate, Score: score, Eligible: eligible, Reason: reason);
        }).Where(x => x.Eligible).OrderByDescending(x => x.Score).ToArray();
        if (candidates.Length == 0) return (false, new AutoAssignResult(false, null, 0m, "No eligible candidate."));
        var selected = candidates[0];
        state.Assignments.Add(new TaskAssignmentRecord
        {
            TaskId = request.TaskId, TaskType = request.TaskType, EmployeeId = selected.Candidate.EmployeeId,
            Score = selected.Score, AssignedAt = clock.GetUtcNow()
        });
        return (true, new AutoAssignResult(true, selected.Candidate.EmployeeId, selected.Score, "Highest deterministic claim score."));
    }, cancellationToken);

    public Task<TaskAssignmentRecord> ReassignTaskAsync(Guid tenantId, Guid taskId, Guid employeeId,
        string reason, string approvalId, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(approvalId))
            throw new InvalidOperationException("Reassignment reason and approval are required.");
        var current = state.Assignments.LastOrDefault(x => x.TaskId == taskId && x.Status == "Assigned")
            ?? throw new KeyNotFoundException("Active task assignment not found.");
        if (current.EmployeeId == employeeId) throw new InvalidOperationException("Employee already owns the task.");
        current.Status = "Reassigned";
        var replacement = new TaskAssignmentRecord
        {
            TaskId = taskId, TaskType = current.TaskType, EmployeeId = employeeId,
            PreviousEmployeeId = current.EmployeeId, Reason = reason, ApprovalId = approvalId,
            AssignedAt = clock.GetUtcNow(), Status = "Assigned"
        };
        state.Assignments.Add(replacement);
        return (true, replacement);
    }, cancellationToken);

    public Task<WorkLedgerEntry> RecordWorkAsync(Guid tenantId, WorkLedgerEntry entry,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var existing = state.Ledger.FirstOrDefault(x => x.EmployeeId == entry.EmployeeId && x.TaskId == entry.TaskId
            && x.RoleRelation == entry.RoleRelation);
        if (existing is not null) return (false, existing);
        entry.TenantId = tenantId;
        entry.WeightedScore = entry.TaskWeight * entry.QualityFactor * entry.CapabilityFitFactor;
        state.Ledger.Add(entry);
        return (true, entry);
    }, cancellationToken);

    public Task<ReviewTaskRecord> CreateReviewAsync(Guid tenantId, ReviewTaskRecord review,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        review.TenantId = tenantId;
        state.Reviews.Add(review);
        return (true, review);
    }, cancellationToken);

    public Task<ReviewTaskRecord> ApproveReviewAsync(Guid tenantId, Guid reviewId, string approver,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var review = Find(state.Reviews, reviewId);
        if (review.Author == approver) throw new InvalidOperationException("Author cannot approve their own review.");
        review.Approvers = [.. review.Approvers, approver];
        review.Status = review.Approvers.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= review.RequiredQuorum ? "Approved" : "Open";
        return (true, review);
    }, cancellationToken);

    public Task<DebateTaskRecord> CreateDebateAsync(Guid tenantId, DebateTaskRecord debate,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        debate.TenantId = tenantId;
        state.Debates.Add(debate);
        return (true, debate);
    }, cancellationToken);

    public Task<DebateTaskRecord> DecideDebateAsync(Guid tenantId, Guid debateId, string decision,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var debate = Find(state.Debates, debateId);
        if (clock.GetUtcNow() > debate.Deadline) throw new InvalidOperationException("Debate deadline exceeded; adjudication required.");
        if (!debate.Options.Contains(decision)) throw new InvalidOperationException("Decision is not an allowed option.");
        debate.Decision = decision;
        debate.Status = "Decided";
        return (true, debate);
    }, cancellationToken);

    public Task<AdjudicationTaskRecord> AdjudicateAsync(Guid tenantId, AdjudicationTaskRecord adjudication,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var debate = Find(state.Debates, adjudication.DebateId);
        if (clock.GetUtcNow() <= debate.Deadline && debate.Status == "Open")
            throw new InvalidOperationException("Debate has not expired.");
        adjudication.TenantId = tenantId;
        debate.Decision = adjudication.Decision;
        debate.Status = "Adjudicated";
        state.Adjudications.Add(adjudication);
        return (true, adjudication);
    }, cancellationToken);

    public Task<TaskGovernanceState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId, Func<TaskGovernanceState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = tenantId.ToString();
            var state = await store.GetAsync(key, cancellationToken) ?? new TaskGovernanceState();
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(key, state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static (decimal Score, bool Eligible, string Reason) CalculateClaimScore(decimal capability,
        int currentWip, int wipLimit, decimal historySuccess, decimal toolFit, decimal costEfficiency, bool independent)
    {
        var eligible = currentWip < wipLimit && independent;
        var score = eligible ? capability * 0.4m + historySuccess * 0.2m + toolFit * 0.1m
            + costEfficiency * 0.1m + (1m - (decimal)currentWip / Math.Max(1, wipLimit)) * 0.1m
            + (independent ? 0.1m : 0m) : 0m;
        return (score, eligible, !independent ? "Conflict of interest." : currentWip >= wipLimit ? "WIP limit reached." : "Eligible.");
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");
}
