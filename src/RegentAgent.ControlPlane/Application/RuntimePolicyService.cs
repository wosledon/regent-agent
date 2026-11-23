using System.Globalization;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class RuntimePolicyService(JsonDocumentStore<RuntimePolicyState> store, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<RuntimePolicyState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    public Task<CostEntry> RecordCostAsync(Guid tenantId, CostEntry cost, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(cost.SourceEventId)) throw new InvalidOperationException("Source event id is required.");
        var existing = state.Costs.FirstOrDefault(x => x.SourceEventId == cost.SourceEventId);
        if (existing is not null) return (false, existing);
        if (cost.Amount < 0 || cost.Currency.Length != 3) throw new InvalidOperationException("Amount or currency is invalid.");
        cost.CreatedAt = clock.GetUtcNow();
        if (cost.ProjectId is null)
        {
            var rule = state.AllocationRules.FirstOrDefault(x => x.SourceCategory == cost.Category && x.Currency == cost.Currency);
            if (rule is not null)
            {
                cost.ProjectId = rule.TargetProjectId;
                cost.Amount *= rule.Percentage / 100m;
                cost.AllocationStatus = "Allocated";
            }
            else cost.AllocationStatus = "Unallocated";
        }
        state.Costs.Add(cost);
        return (true, cost);
    }, cancellationToken);

    public Task<CostEntry> CorrectCostAsync(Guid tenantId, Guid originalId, decimal delta, string reason,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var original = state.Costs.SingleOrDefault(x => x.Id == originalId) ?? throw new KeyNotFoundException("Cost entry not found.");
        var correction = new CostEntry
        {
            SourceEventId = $"correction:{originalId}:{Guid.NewGuid():N}", TaskId = original.TaskId,
            ProjectId = original.ProjectId, EmployeeId = original.EmployeeId, Model = original.Model,
            Amount = delta, Currency = original.Currency, Category = "Correction",
            AllocationStatus = original.AllocationStatus, CorrectionOf = originalId,
            CreatedAt = clock.GetUtcNow(), Reason = reason
        };
        state.Costs.Add(correction);
        return (true, correction);
    }, cancellationToken);

    public Task<BudgetPolicy> SetBudgetAsync(Guid tenantId, BudgetPolicy budget, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        if (budget.SoftLimit < 0 || budget.HardLimit < budget.SoftLimit) throw new InvalidOperationException("Budget limits are invalid.");
        state.Budgets.RemoveAll(x => x.ProjectId == budget.ProjectId && x.Currency == budget.Currency);
        state.Budgets.Add(budget);
        return (true, budget);
    }, cancellationToken);

    public Task<AllocationRule> AddAllocationRuleAsync(Guid tenantId, AllocationRule rule, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        if (rule.Percentage is <= 0 or > 100) throw new InvalidOperationException("Allocation percentage must be in (0, 100].");
        state.AllocationRules.Add(rule);
        return (true, rule);
    }, cancellationToken);

    public Task<CostSummaryDto> SummaryAsync(Guid tenantId, Guid? projectId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        var costs = state.Costs.Where(x => projectId is null || x.ProjectId == projectId).ToArray();
        var totals = costs.GroupBy(x => x.Currency).Select(x => new CurrencyTotal(x.Key, x.Sum(v => v.Amount))).ToArray();
        var warnings = new List<string>();
        foreach (var budget in state.Budgets.Where(x => projectId is null || x.ProjectId == projectId))
        {
            var spent = costs.Where(x => x.ProjectId == budget.ProjectId && x.Currency == budget.Currency).Sum(x => x.Amount);
            if (spent >= budget.HardLimit) warnings.Add($"{budget.ProjectId}: hard budget reached in {budget.Currency}.");
            else if (spent >= budget.SoftLimit) warnings.Add($"{budget.ProjectId}: soft budget reached in {budget.Currency}.");
        }
        return (false, new CostSummaryDto(totals, costs.Count(x => x.AllocationStatus == "Unallocated"), warnings));
    }, cancellationToken);

    public Task<bool> CanSpendAsync(Guid tenantId, Guid projectId, decimal amount, string currency,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var budget = state.Budgets.FirstOrDefault(x => x.ProjectId == projectId && x.Currency == currency);
        if (budget is null) return (false, true);
        var spent = state.Costs.Where(x => x.ProjectId == projectId && x.Currency == currency).Sum(x => x.Amount);
        return (false, spent + amount <= budget.HardLimit);
    }, cancellationToken);

    public Task<WorkSchedule> UpdateScheduleAsync(Guid tenantId, WorkSchedule schedule, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        schedule.Version++;
        state.Schedule = schedule;
        return (true, schedule);
    }, cancellationToken);

    public Task<DispatchDecision> DecideDispatchAsync(Guid tenantId, DateTimeOffset now, bool emergency,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var schedule = state.Schedule;
        if (emergency && schedule.EmergencyWake) return (false, new DispatchDecision(true, "Emergency wake", null, false));
        if (schedule.AlwaysAvailable) return (false, new DispatchDecision(true, "24x7 schedule", null, false));
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, timeZone);
        var day = local.ToString("ddd", CultureInfo.InvariantCulture);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var active = schedule.Windows.FirstOrDefault(x => x.Days.Split(',').Contains(day)
            && TimeOnly.Parse(x.StartLocal, CultureInfo.InvariantCulture) <= time
            && time <= TimeOnly.Parse(x.EndLocal, CultureInfo.InvariantCulture));
        if (active is null) return (false, new DispatchDecision(true, "Outside restricted windows", null, false));
        if (active.Kind == "Cheap") return (false, new DispatchDecision(true, "Cheap-price window", active.EndLocal, true));
        return (false, new DispatchDecision(false, "Silent window", active.EndLocal, false));
    }, cancellationToken);

    public Task<CapacityPolicy> UpdateCapacityAsync(Guid tenantId, CapacityPolicy capacity, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        if (capacity.MaxConcurrentTasks <= 0 || capacity.MaxQueueLength <= 0) throw new InvalidOperationException("Capacity limits must be positive.");
        capacity.Version++;
        state.Capacity = capacity;
        return (true, capacity);
    }, cancellationToken);

    public Task<CapacityDecision> AdmitQueueItemAsync(Guid tenantId, int queueDepth, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        var accepted = queueDepth < state.Capacity.MaxQueueLength;
        return (false, new CapacityDecision(accepted, accepted ? "Queue has capacity." : "Queue backpressure applied.",
            queueDepth, state.Capacity.MaxQueueLength));
    }, cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId, Func<RuntimePolicyState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = tenantId.ToString();
            var state = await store.GetAsync(key, cancellationToken) ?? new RuntimePolicyState { TenantId = tenantId };
            if (state.TenantId != tenantId) throw new UnauthorizedAccessException("Runtime policy belongs to another tenant.");
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(key, state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }
}
