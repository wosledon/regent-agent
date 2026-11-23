namespace RegentAgent.Contracts.Operations;

public sealed class CostEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceEventId { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Model { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "CNY";
    public string Category { get; set; } = "Model";
    public string AllocationStatus { get; set; } = "Direct";
    public Guid? CorrectionOf { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BudgetPolicy
{
    public Guid ProjectId { get; set; }
    public decimal SoftLimit { get; set; }
    public decimal HardLimit { get; set; }
    public string Currency { get; set; } = "CNY";
    public bool CarryForward { get; set; }
}

public sealed class AllocationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourceCategory { get; set; } = string.Empty;
    public Guid TargetProjectId { get; set; }
    public decimal Percentage { get; set; }
    public string Currency { get; set; } = "CNY";
}

public sealed record CurrencyTotal(string Currency, decimal Amount);
public sealed record CostSummaryDto(IReadOnlyList<CurrencyTotal> Totals, int UnallocatedCount, IReadOnlyList<string> Warnings);

public sealed class WorkWindow
{
    public string Days { get; set; } = "Mon,Tue,Wed,Thu,Fri";
    public string StartLocal { get; set; } = "09:00";
    public string EndLocal { get; set; } = "17:00";
    public string Kind { get; set; } = "Silent";
}

public sealed class WorkSchedule
{
    public string TimeZone { get; set; } = "Asia/Shanghai";
    public bool AlwaysAvailable { get; set; } = true;
    public IReadOnlyList<WorkWindow> Windows { get; set; } = [];
    public bool EmergencyWake { get; set; } = true;
    public int Version { get; set; } = 1;
}

public sealed record DispatchDecision(bool RunNow, string Reason, string? ResumeAtLocal, bool CheapWindow);

public sealed class CapacityPolicy
{
    public int MaxConcurrentTasks { get; set; } = 8;
    public int MaxQueueLength { get; set; } = 100;
    public int ApiP95TargetMs { get; set; } = 500;
    public int NoisyNeighborCpuPercent { get; set; } = 80;
    public int Version { get; set; } = 1;
}

public sealed record CapacityDecision(bool Accepted, string Reason, int QueueDepth, int MaxQueueLength);

public sealed class RuntimePolicyState
{
    public Guid TenantId { get; set; }
    public List<CostEntry> Costs { get; set; } = [];
    public List<BudgetPolicy> Budgets { get; set; } = [];
    public List<AllocationRule> AllocationRules { get; set; } = [];
    public WorkSchedule Schedule { get; set; } = new();
    public CapacityPolicy Capacity { get; set; } = new();
}
