namespace RegentAgent.Contracts.Models;

public sealed record ModelSnapshot(string Id, DateOnly SnapshotDate, string Source, bool Immutable)
{
    public string? RawJson { get; init; }
    public string? Checksum { get; init; }
    public DateTimeOffset? FetchedAt { get; init; }
}

public sealed class ModelCatalogEntry
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string PriceState { get; set; } = "Unpriced";
    public decimal? InputPricePerMillion { get; set; }
    public decimal? OutputPricePerMillion { get; set; }
    public string Currency { get; set; } = "USD";
    public string Tier { get; set; } = "Unassigned";
    public string Source { get; set; } = "local-snapshot";
}

public sealed class ManualModelPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public decimal InputPricePerMillion { get; set; }
    public decimal OutputPricePerMillion { get; set; }
    public string Currency { get; set; } = "USD";
    public string Reason { get; set; } = string.Empty;
}

public sealed class ModelTierAssignment
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Tier { get; set; } = "Unassigned";
    public int Version { get; set; } = 1;
}

public sealed class EmployeeModelBinding
{
    public Guid EmployeeId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ReasoningEffort { get; set; } = "medium";
    public string Protocol { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
}

public sealed class ModelUnavailableEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int ConsecutiveFailures { get; set; }
    public int UnavailableMinutes { get; set; }
    public string Escalation { get; set; } = "Leader";
    public DateTimeOffset DetectedAt { get; set; }
}

public sealed record ModelTestResult(string Provider, string ModelId, string Protocol, bool Supported, string Message);

public sealed class ModelPoolState
{
    public List<ModelSnapshot> Snapshots { get; set; } = [];
    public List<ModelCatalogEntry> Catalog { get; set; } = [];
    public List<ManualModelPrice> ManualPrices { get; set; } = [];
    public List<ModelTierAssignment> TierAssignments { get; set; } = [];
    public List<EmployeeModelBinding> Bindings { get; set; } = [];
    public List<ModelUnavailableEvent> UnavailableEvents { get; set; } = [];
}
