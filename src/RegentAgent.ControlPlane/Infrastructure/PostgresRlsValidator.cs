using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public static class PostgresRlsValidator
{
    public static async Task ValidateAsync(RegentDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true) return;
        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("TenantId") is not null)
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            foreach (var table in tables)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = """
                    SELECT c.relrowsecurity, c.relforcerowsecurity
                    FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = current_schema() AND c.relname = @table
                    """;
                var parameter = command.CreateParameter();
                parameter.ParameterName = "table";
                parameter.Value = table;
                command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0) || !reader.GetBoolean(1))
                    throw new InvalidOperationException($"Postgres tenant table {table} must enable and force RLS.");
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
