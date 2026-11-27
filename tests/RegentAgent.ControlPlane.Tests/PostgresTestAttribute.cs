using Xunit;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")))
            Skip = "REGENT_TEST_POSTGRES_DSN is not configured.";
    }
}
