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
    string? TaskId = null);

public interface IUsageJournal
{
    Task AppendAsync(UsageRecord record, CancellationToken cancellationToken);
}

public sealed class JsonlUsageJournal : IUsageJournal
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonlUsageJournal(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task AppendAsync(UsageRecord record, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(record) + "\n", cancellationToken);
        }
        finally { gate.Release(); }
    }
}
