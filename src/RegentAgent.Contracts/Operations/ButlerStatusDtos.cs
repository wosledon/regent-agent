namespace RegentAgent.Contracts.Operations;

public sealed class ButlerReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = "Daily";
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public IReadOnlyList<string> EvidenceRefs { get; set; } = [];
    public string Status { get; set; } = "Completed";
    public int Attempts { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ReportSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = "Daily";
    public string Frequency { get; set; } = "Daily";
    public bool Enabled { get; set; } = true;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public string? LastError { get; set; }
}

public sealed record ButlerAnswer(string Answer, IReadOnlyList<string> EvidenceRefs, string Intent);
public sealed record CreateReportScheduleRequest(string Kind, string Frequency, bool Enabled);
public sealed record ButlerQuestionRequest(string Question);
public sealed record GenerateReportRequest(string Kind);

public sealed class PlatformComponentStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public int LatencyMs { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class PlatformIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Severity { get; set; } = "SEV3";
    public string Status { get; set; } = "Open";
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed record PlatformStatusDto(string OverallStatus, DateTimeOffset CheckedAt,
    IReadOnlyList<PlatformComponentStatus> Components, IReadOnlyList<PlatformIncident> Incidents);
public sealed record TenantStatusRequest(string Status);
