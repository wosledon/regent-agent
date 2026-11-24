using System.Runtime.CompilerServices;
using System.Text;

namespace RegentAgent.Providers;

public static class ProviderSseReader
{
    public static async IAsyncEnumerable<ProviderStreamChunk> ReadAsync(Stream stream, int maxBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? eventType = null;
        var data = new StringBuilder();
        long totalBytes = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            totalBytes += Encoding.UTF8.GetByteCount(line) + 1;
            if (totalBytes > maxBytes)
                throw new ProviderRequestException(null, "PROVIDER_RESPONSE_TOO_LARGE", false, "Provider stream exceeded the configured limit.");
            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                var payload = data.ToString().TrimEnd('\r', '\n');
                data.Clear();
                var currentEvent = eventType;
                eventType = null;
                yield return new ProviderStreamChunk(payload, currentEvent);
                if (payload == "[DONE]") yield break;
                continue;
            }
            if (line.StartsWith(':')) continue;
            var separator = line.IndexOf(':');
            var field = separator < 0 ? line : line[..separator];
            var value = separator < 0 ? string.Empty : line[(separator + 1)..].TrimStart();
            if (field == "event") eventType = value;
            else if (field == "data") data.AppendLine(value);
        }
        if (data.Length > 0) yield return new ProviderStreamChunk(data.ToString().TrimEnd('\r', '\n'), eventType);
    }
}
