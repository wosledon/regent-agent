using System.Text.RegularExpressions;
using Npgsql;

namespace RegentAgent.Gateway.KeyVault;

public sealed class PostgresKeyVaultOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string Schema { get; set; } = "keyvault";
    public string Table { get; set; } = "model_keys";
    public int CommandTimeoutSeconds { get; set; } = 15;
    public bool RequireTls { get; set; } = true;

    public string QualifiedTable => $"\"{Schema}\".\"{Table}\"";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException("Postgres keyvault connection string is required.");
        if (!IdentifierRegex().IsMatch(Schema) || !IdentifierRegex().IsMatch(Table))
            throw new InvalidOperationException("Postgres keyvault schema and table must be SQL identifiers.");
        if (CommandTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException("Postgres keyvault command timeout is invalid.");
        var connection = new NpgsqlConnectionStringBuilder(ConnectionString);
        if (RequireTls && connection.SslMode is not (SslMode.Require or SslMode.VerifyCA or SslMode.VerifyFull))
            throw new InvalidOperationException("Postgres keyvault requires TLS.");
    }

    private static Regex IdentifierRegex() => new("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);
}
