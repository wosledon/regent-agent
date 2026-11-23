using System.Collections.Concurrent;

namespace RegentAgent.ControlPlane.Application;

public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> attempts = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;

    public bool TryAcquire(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var window = attempts.GetOrAdd(key, _ => new AttemptWindow(now, 0));
        lock (window)
        {
            if (now - window.StartedAt >= Window)
            {
                window.StartedAt = now;
                window.Count = 0;
            }
            if (window.Count >= MaxAttempts) return false;
            window.Count++;
            return true;
        }
    }

    private sealed class AttemptWindow(DateTimeOffset startedAt, int count)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Count { get; set; } = count;
    }
}
