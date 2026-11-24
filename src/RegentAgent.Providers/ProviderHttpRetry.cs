using System.Net;

namespace RegentAgent.Providers;

internal static class ProviderHttpRetry
{
    public static bool IsRetryable(HttpStatusCode statusCode) =>
        (int)statusCode is 408 or 429 or >= 500;

    public static TimeSpan Delay(int attempt, int baseDelayMilliseconds)
    {
        var milliseconds = Math.Min(5000, baseDelayMilliseconds * Math.Pow(2, attempt));
        var jitter = milliseconds == 0 ? 0 : Random.Shared.Next(0, Math.Max(1, (int)milliseconds / 2));
        return TimeSpan.FromMilliseconds(milliseconds + jitter);
    }

    public static TimeSpan Delay(HttpResponseMessage response, int attempt, int baseDelayMilliseconds)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            var delay = retryAfter.Delta ?? retryAfter.Date - DateTimeOffset.UtcNow;
            if (delay is { } value && value > TimeSpan.Zero) return value > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30) : value;
        }
        return Delay(attempt, baseDelayMilliseconds);
    }
}
