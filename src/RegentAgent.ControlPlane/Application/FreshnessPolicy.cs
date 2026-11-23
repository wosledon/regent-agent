using RegentAgent.Contracts.Enums;

namespace RegentAgent.ControlPlane.Application;

public static class FreshnessPolicy
{
    public static Freshness Evaluate(DateTimeOffset dataAt, DateTimeOffset now, TimeSpan staleAfter)
    {
        var age = now - dataAt;
        return age <= TimeSpan.FromMinutes(1) ? Freshness.Live
            : age <= staleAfter ? Freshness.Fresh
            : Freshness.Stale;
    }
}
