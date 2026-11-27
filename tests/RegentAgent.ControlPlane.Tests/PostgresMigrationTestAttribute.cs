using Xunit;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class PostgresMigrationFactAttribute : FactAttribute
{
    public PostgresMigrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")))
            Skip = "REGENT_TEST_POSTGRES_MIGRATION_DSN is not configured.";
    }
}

internal sealed class PostgresMigrationTheoryAttribute : TheoryAttribute
{
    public PostgresMigrationTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")))
            Skip = "REGENT_TEST_POSTGRES_MIGRATION_DSN is not configured.";
    }
}
