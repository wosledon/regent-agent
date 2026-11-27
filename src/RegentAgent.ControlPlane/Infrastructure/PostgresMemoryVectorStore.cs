using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;
using RegentAgent.Memory;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class PostgresMemoryVectorStore(string connectionString) : IMemoryVectorStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource = CreateDataSource(connectionString);

    public ValueTask DisposeAsync() => dataSource.DisposeAsync();

    public Task<MemoryVectorRecord?> GetAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        _ = MemoryVectorFormat.ValidateClaimId(claimId);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, """
                SELECT "ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector"::text, "IndexedAt"
                FROM "MemoryEmbeddings"
                WHERE "TenantId" = @tenant_id AND "ClaimId" = @claim_id;
                """);
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenant });
            command.Parameters.Add(new NpgsqlParameter("claim_id", claimId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
        }, cancellationToken);
    }

    public Task PutAsync(MemoryVectorRecord record, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateRecord(record);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, """
                INSERT INTO "MemoryEmbeddings"
                    ("ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector", "IndexedAt")
                VALUES
                    (@claim_id, @tenant_id, @content_hash, @model_version, CAST(@vector AS vector), @indexed_at)
                ON CONFLICT ("TenantId", "ClaimId") DO UPDATE SET
                    "ContentHash" = EXCLUDED."ContentHash",
                    "ModelVersion" = EXCLUDED."ModelVersion",
                    "Vector" = EXCLUDED."Vector",
                    "IndexedAt" = EXCLUDED."IndexedAt";
                """);
            command.Parameters.Add(new NpgsqlParameter("claim_id", record.ClaimId));
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenant });
            command.Parameters.Add(new NpgsqlParameter("content_hash", record.ContentHash));
            command.Parameters.Add(new NpgsqlParameter("model_version", record.ModelVersion));
            command.Parameters.Add(new NpgsqlParameter("vector", PostgresVectorLiteral.Serialize(record.Vector)));
            command.Parameters.Add(new NpgsqlParameter("indexed_at", NpgsqlDbType.TimestampTz) { Value = record.IndexedAt });
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public Task DeleteAsync(string tenantId, string claimId, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        _ = MemoryVectorFormat.ValidateClaimId(claimId);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, """
                DELETE FROM "MemoryEmbeddings"
                WHERE "TenantId" = @tenant_id AND "ClaimId" = @claim_id;
                """);
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenant });
            command.Parameters.Add(new NpgsqlParameter("claim_id", claimId));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryVectorRecord>> GetAllAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, """
                SELECT "ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector"::text, "IndexedAt"
                FROM "MemoryEmbeddings"
                WHERE "TenantId" = @tenant_id
                ORDER BY "ClaimId";
                """);
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenant });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var records = new List<MemoryVectorRecord>();
            while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader));
            return (IReadOnlyList<MemoryVectorRecord>)records;
        }, cancellationToken);
    }

    public Task<int> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateCommand(connection, transaction, """
                DELETE FROM "MemoryEmbeddings" WHERE "TenantId" = @tenant_id;
                """);
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenant });
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<MemoryVectorRecord>> SearchAsync(string tenantId, float[] queryVector,
        IReadOnlyList<string> allowedClaimIds, int maxResults, CancellationToken cancellationToken)
    {
        var tenant = MemoryVectorFormat.ValidateTenantId(tenantId);
        MemoryVectorFormat.ValidateSearch(queryVector, allowedClaimIds, maxResults);
        return WithTenantAsync(tenant, async (connection, transaction) =>
        {
            await using var command = CreateSearchCommand(connection, transaction, tenant, queryVector, allowedClaimIds, maxResults);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var records = new List<MemoryVectorRecord>();
            while (await reader.ReadAsync(cancellationToken)) records.Add(ReadRecord(reader));
            return (IReadOnlyList<MemoryVectorRecord>)records;
        }, cancellationToken);
    }

    // The ACL candidate set is capped at 200; materialize it for exact pgvector distance ordering
    // instead of allowing approximate HNSW pruning to drop an authorized candidate.
    internal static NpgsqlCommand CreateSearchCommand(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, float[] queryVector, IReadOnlyList<string> allowedClaimIds, int maxResults)
    {
        var command = CreateCommand(connection, transaction, """
            WITH "MemoryCandidates" AS MATERIALIZED (
                SELECT "ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector", "IndexedAt"
                FROM "MemoryEmbeddings"
                WHERE "TenantId" = @tenant_id AND "ClaimId" = ANY(@claim_ids)
            )
            SELECT "ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector"::text, "IndexedAt"
            FROM "MemoryCandidates"
            ORDER BY "Vector" <=> CAST(@query_vector AS vector)
            LIMIT @max_results;
            """);
        command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Uuid) { Value = tenantId });
        command.Parameters.Add(new NpgsqlParameter("claim_ids", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = allowedClaimIds.ToArray()
        });
        command.Parameters.Add(new NpgsqlParameter("query_vector", PostgresVectorLiteral.Serialize(queryVector)));
        command.Parameters.Add(new NpgsqlParameter("max_results", maxResults));
        return command;
    }

    private static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Postgres connection string is required.", nameof(connectionString));
        return NpgsqlDataSource.Create(connectionString);
    }

    private async Task<T> WithTenantAsync<T>(Guid tenant,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = CreateCommand(connection, transaction, "SELECT set_config('app.tenant_id', @tenant_id, true);"))
        {
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlDbType.Text) { Value = tenant.ToString("D") });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        var result = await action(connection, transaction);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static NpgsqlCommand CreateCommand(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static MemoryVectorRecord ReadRecord(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetGuid(1).ToString("D"), reader.GetString(2), reader.GetString(3),
        PostgresVectorLiteral.Parse(reader.GetString(4)), reader.GetFieldValue<DateTimeOffset>(5));
}

internal static class PostgresVectorLiteral
{
    public static string Serialize(float[] vector)
    {
        MemoryVectorFormat.ValidateVector(vector);
        var literal = new StringBuilder(vector.Length * 12 + 2).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) literal.Append(',');
            literal.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return literal.Append(']').ToString();
    }

    public static float[] Parse(string literal)
    {
        if (string.IsNullOrWhiteSpace(literal) || literal[0] != '[' || literal[^1] != ']')
            throw new InvalidDataException("Stored vector is invalid.");
        var values = literal[1..^1].Split(',');
        if (values.Length != MemoryVectorFormat.Dimensions) throw new InvalidDataException("Stored vector is invalid.");
        var vector = new float[MemoryVectorFormat.Dimensions];
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value))
                throw new InvalidDataException("Stored vector is invalid.");
            vector[i] = value;
        }
        MemoryVectorFormat.ValidateVector(vector);
        return vector;
    }
}
