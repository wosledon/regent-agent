using System.Diagnostics;

namespace RegentAgent.Observability;

public static class RegentActivitySource
{
    public const string Name = "RegentAgent";
    public static readonly ActivitySource Instance = new(Name);
}
