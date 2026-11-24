using System.Text.Json;

namespace RegentAgent.Gateway.Metering;

public sealed record UsageRecord(
    DateTimeOffset OccurredAt,
    string TenantId,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    string ProviderRequestId,
    string? EmployeeId = null,
    string? TaskId = null)
{
    public Guid? ReservationId { get; init; }
    public string? UsageEventId { get; init; }
    public string? RequestId { get; init; }
    public int CachedInputTokens { get; init; }
    public int ReasoningTokens { get; init; }
    public long AmountMinor { get; init; }
    public string? Currency { get; init; }
    public string? PriceVersion { get; init; }
    public string? OccurredPeriod { get; init; }
    public string? PostedPeriod { get; init; }
    public string Status { get; init; } = "Succeeded";
    public int Attempt { get; init; } = 1;
    public string? ErrorCode { get; init; }
}

public interface IUsageJournal
{
    Task AppendAsync(UsageRecord record, CancellationToken cancellationToken);
}

public sealed class JsonlUsageJournal : IUsageJournal
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> usageEventIds = new(StringComparer.Ordinal);

    public JsonlUsageJournal(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<UsageRecord>(line);
                    if (record?.UsageEventId is { } id) usageEventIds.Add(id);
                }
                catch (JsonException) { }
            }
    }

    public async Task AppendAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (record.UsageEventId is { } id && !usageEventIds.Add(id)) return;
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record) + "\n", cancellationToken);
        }
        finally { gate.Release(); }
    }
}
