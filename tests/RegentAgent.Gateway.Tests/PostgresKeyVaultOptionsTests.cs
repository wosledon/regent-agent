using RegentAgent.Gateway.KeyVault;
using Xunit;

namespace RegentAgent.Gateway.Tests;

public sealed class PostgresKeyVaultOptionsTests
{
    [Fact]
    public void Postgres_keyvault_requires_connection_tls_and_safe_identifiers()
    {
        var options = new PostgresKeyVaultOptions { ConnectionString = "Host=db.example;SslMode=VerifyFull" };
        options.Validate();

        Assert.Throws<InvalidOperationException>(() => new PostgresKeyVaultOptions().Validate());
        Assert.Throws<InvalidOperationException>(() => new PostgresKeyVaultOptions
        {
            ConnectionString = "Host=db.example;SslMode=Disable"
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new PostgresKeyVaultOptions
        {
            ConnectionString = "Host=db.example;SslMode=VerifyFull", Schema = "keyvault;DROP TABLE model_keys"
        }.Validate());
    }
}
