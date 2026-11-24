using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace RegentAgent.Gateway.KeyVault;

public sealed class PostgresKeyVault(NpgsqlDataSource dataSource, IMasterKeyProvider masterKeyProvider,
    PostgresKeyVaultOptions options) : IKeyVault
{
    private readonly NpgsqlDataSource dataSource = dataSource;
    private readonly byte[] masterKey = masterKeyProvider.GetMasterKey();
    private readonly PostgresKeyVaultOptions options = options;
    private const string Columns = "key_ref, tenant_id, provider, model, version, state, created_at, encrypted_key, key_nonce, key_tag, wrapped_data_key, wrap_nonce, wrap_tag";

    public async Task<KeyVersionInfo> SetAsync(string tenantId, string provider, string model, string apiKey,
        CancellationToken cancellationToken)
    {
        var tenant = ParseTenant(tenantId);
        var record = KeyEnvelope.Encrypt(masterKey, tenantId, provider, model, apiKey, 1);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenant, cancellationToken);
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {options.QualifiedTable} ({Columns}) VALUES (@key_ref, @tenant_id, @provider, @model, @version, @state, @created_at, @encrypted_key, @key_nonce, @key_tag, @wrapped_data_key, @wrap_nonce, @wrap_tag)",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        AddRecordParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return EncryptedKeyRecordMapper.ToInfo(record);
    }

    public async Task<KeyVersionInfo> RotateAsync(string tenantId, string keyRef, string apiKey,
        CancellationToken cancellationToken)
    {
        var tenant = ParseTenant(tenantId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenant, cancellationToken);
        var current = await LoadForUpdateAsync(connection, transaction, tenant, keyRef, cancellationToken);
        await using (var retire = new NpgsqlCommand(
            $"UPDATE {options.QualifiedTable} SET state = 'Retired' WHERE tenant_id = @tenant_id AND key_ref = @key_ref",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds })
        {
            retire.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenant);
            retire.Parameters.AddWithValue("key_ref", NpgsqlDbType.Text, keyRef);
            await retire.ExecuteNonQueryAsync(cancellationToken);
        }
        var next = KeyEnvelope.Encrypt(masterKey, current.TenantId, current.Provider, current.Model, apiKey,
            current.Version + 1, current.KeyRef);
        await using (var insert = new NpgsqlCommand(
            $"INSERT INTO {options.QualifiedTable} ({Columns}) VALUES (@key_ref, @tenant_id, @provider, @model, @version, @state, @created_at, @encrypted_key, @key_nonce, @key_tag, @wrapped_data_key, @wrap_nonce, @wrap_tag)",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds })
        {
            AddRecordParameters(insert, next);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return EncryptedKeyRecordMapper.ToInfo(next);
    }

    public async Task<string?> UnlockAsync(string tenantId, string keyRef, CancellationToken cancellationToken)
    {
        var tenant = ParseTenant(tenantId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenant, cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM {options.QualifiedTable} WHERE tenant_id = @tenant_id AND key_ref = @key_ref AND state = 'Active'",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenant);
        command.Parameters.AddWithValue("key_ref", NpgsqlDbType.Text, keyRef);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var record = ReadRecord(reader);
        var dataKey = AesGcmCrypt.Unwrap(masterKey, record.WrappedDataKey, record.WrapNonce, record.WrapTag);
        return AesGcmCrypt.Unlock(dataKey, record.EncryptedKey, record.KeyNonce, record.KeyTag);
    }

    public async Task DeleteAsync(string tenantId, string keyRef, CancellationToken cancellationToken)
    {
        var tenant = ParseTenant(tenantId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenant, cancellationToken);
        await using var command = new NpgsqlCommand(
            $"DELETE FROM {options.QualifiedTable} WHERE tenant_id = @tenant_id AND key_ref = @key_ref",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenant);
        command.Parameters.AddWithValue("key_ref", NpgsqlDbType.Text, keyRef);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KeyVersionInfo>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = ParseTenant(tenantId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTenantScopeAsync(connection, transaction, tenant, cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM {options.QualifiedTable} WHERE tenant_id = @tenant_id ORDER BY created_at, version",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenant);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<KeyVersionInfo>();
        while (await reader.ReadAsync(cancellationToken)) records.Add(EncryptedKeyRecordMapper.ToInfo(ReadRecord(reader)));
        return records;
    }

    private async Task<EncryptedKeyRecord> LoadForUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, string keyRef, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {Columns} FROM {options.QualifiedTable} WHERE tenant_id = @tenant_id AND key_ref = @key_ref ORDER BY version DESC LIMIT 1 FOR UPDATE",
            connection, transaction) { CommandTimeout = options.CommandTimeoutSeconds };
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        command.Parameters.AddWithValue("key_ref", NpgsqlDbType.Text, keyRef);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("Key reference not found.");
        return ReadRecord(reader);
    }

    private static async Task SetTenantScopeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SET LOCAL app.tenant_id = @tenant_id", connection, transaction);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, tenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid ParseTenant(string tenantId) => Guid.TryParse(tenantId, out var tenant)
        ? tenant : throw new InvalidOperationException("Postgres keyvault tenant must be a UUID.");

    private static void AddRecordParameters(NpgsqlCommand command, EncryptedKeyRecord record)
    {
        command.Parameters.AddWithValue("key_ref", NpgsqlDbType.Text, record.KeyRef);
        command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Uuid, Guid.Parse(record.TenantId));
        command.Parameters.AddWithValue("provider", NpgsqlDbType.Text, record.Provider);
        command.Parameters.AddWithValue("model", NpgsqlDbType.Text, record.Model);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, record.Version);
        command.Parameters.AddWithValue("state", NpgsqlDbType.Text, record.State);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, record.CreatedAt);
        command.Parameters.AddWithValue("encrypted_key", NpgsqlDbType.Text, record.EncryptedKey);
        command.Parameters.AddWithValue("key_nonce", NpgsqlDbType.Text, record.KeyNonce);
        command.Parameters.AddWithValue("key_tag", NpgsqlDbType.Text, record.KeyTag);
        command.Parameters.AddWithValue("wrapped_data_key", NpgsqlDbType.Text, record.WrappedDataKey);
        command.Parameters.AddWithValue("wrap_nonce", NpgsqlDbType.Text, record.WrapNonce);
        command.Parameters.AddWithValue("wrap_tag", NpgsqlDbType.Text, record.WrapTag);
    }

    private static EncryptedKeyRecord ReadRecord(NpgsqlDataReader reader) => new()
    {
        KeyRef = reader.GetString(0), TenantId = reader.GetGuid(1).ToString(), Provider = reader.GetString(2),
        Model = reader.GetString(3), Version = reader.GetInt32(4), State = reader.GetString(5),
        CreatedAt = reader.GetFieldValue<DateTimeOffset>(6), EncryptedKey = reader.GetString(7), KeyNonce = reader.GetString(8),
        KeyTag = reader.GetString(9), WrappedDataKey = reader.GetString(10), WrapNonce = reader.GetString(11),
        WrapTag = reader.GetString(12)
    };
}
