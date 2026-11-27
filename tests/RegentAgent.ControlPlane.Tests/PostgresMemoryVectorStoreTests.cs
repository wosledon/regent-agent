using Npgsql;
using RegentAgent.Contracts.Memory;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("Postgres runtime")]
public sealed class PostgresMemoryVectorStoreTests
{
    private const string UnusedConnection = "Host=localhost;Database=unused;Username=unused;Password=unused;Timeout=1";

    [Fact]
    public void Vector_literal_round_trips_with_invariant_numbers()
    {
        var vector = Axis(0);
        vector[1] = 0.1f;
        vector[2] = -2.5f;

        var literal = PostgresVectorLiteral.Serialize(vector);

        Assert.Contains("0.1", literal, StringComparison.Ordinal);
        Assert.Equal(vector, PostgresVectorLiteral.Parse(literal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("[NaN]")]
    [InlineData("[1,2,]")]
    public void Vector_literal_rejects_malformed_dimensions_and_non_finite_values(string literal)
    {
        if (literal == "[1,2,]")
            literal = string.Join(',', Enumerable.Repeat("1", MemoryVectorFormat.Dimensions)) + ",]";
        Assert.Throws<InvalidDataException>(() => PostgresVectorLiteral.Parse(literal));
    }

    [Fact]
    public async Task Store_fails_closed_before_connecting_for_invalid_tenant_or_vector()
    {
        await using var store = new PostgresMemoryVectorStore(UnusedConnection);
        var valid = VectorRecord("claim", "11111111-1111-1111-1111-111111111111", Axis(0));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutAsync(valid with { TenantId = "invalid" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutAsync(valid with { Vector = new float[MemoryVectorFormat.Dimensions - 1] }, CancellationToken.None));
        var nonFinite = Axis(0);
        nonFinite[0] = float.NaN;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutAsync(valid with { Vector = nonFinite }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.PutAsync(valid with { ClaimId = "" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetAsync("invalid", "claim", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.DeleteAsync(valid.TenantId, "", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync("invalid", Axis(0), ["claim"], 1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync(valid.TenantId, nonFinite, ["claim"], 1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SearchAsync(valid.TenantId, Axis(0), [""], 1, CancellationToken.None));
    }

    [PostgresFact]
    public async Task Postgres_store_enforces_rls_crud_cosine_order_and_tenant_delete()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        await using var store = new PostgresMemoryVectorStore(connectionString);
        var tenantA = $"{{{Guid.NewGuid().ToString("D").ToUpperInvariant()}}}";
        var tenantB = Guid.NewGuid().ToString("N").ToUpperInvariant();
        const string claimId = "synthetic-shared-claim";
        try
        {
            await store.PutAsync(VectorRecord(claimId, tenantA, Axis(0)), CancellationToken.None);
            await store.PutAsync(VectorRecord(claimId, tenantB, Axis(1)), CancellationToken.None);
            await store.PutAsync(VectorRecord("orthogonal", tenantA, Axis(1)), CancellationToken.None);
            await store.PutAsync(VectorRecord("opposite", tenantA, NegativeAxis(0)), CancellationToken.None);

            var stored = (await store.GetAsync(Guid.Parse(tenantA).ToString("D"), claimId, CancellationToken.None))!;
            Assert.Equivalent(Axis(0), stored.Vector);
            Assert.Equal(Guid.Parse(tenantA).ToString("D"), stored.TenantId);
            Assert.NotNull(await store.GetAsync(tenantA, claimId, CancellationToken.None));
            Assert.Null(await store.GetAsync(tenantB, "orthogonal", CancellationToken.None));
            var hits = await store.SearchAsync(tenantA, Axis(0), [claimId, "orthogonal", "opposite"], 2,
                CancellationToken.None);
            Assert.Equal([claimId, "orthogonal"], hits.Select(hit => hit.ClaimId));
            await AssertRlsAndSchemaAsync(dataSource, tenantA, tenantB, claimId);

            await store.PutAsync(VectorRecord("opposite", tenantA, Axis(1)) with { ContentHash = "updated-hash" },
                CancellationToken.None);
            Assert.Equal("updated-hash", (await store.GetAsync(tenantA, "opposite", CancellationToken.None))!.ContentHash);
            Assert.Equivalent(Axis(1), (await store.GetAsync(tenantA, "opposite", CancellationToken.None))!.Vector);
            await store.DeleteAsync(tenantB, "orthogonal", CancellationToken.None);
            Assert.NotNull(await store.GetAsync(tenantA, "orthogonal", CancellationToken.None));
            await store.DeleteAsync(tenantA, "orthogonal", CancellationToken.None);
            Assert.Null(await store.GetAsync(tenantA, "orthogonal", CancellationToken.None));
            Assert.Equal(2, await store.DeleteTenantAsync(tenantA, CancellationToken.None));
            Assert.Empty(await store.GetAllAsync(tenantA, CancellationToken.None));
            Assert.Single(await store.GetAllAsync(tenantB, CancellationToken.None));
        }
        finally
        {
            await store.DeleteTenantAsync(tenantA, CancellationToken.None);
            await store.DeleteTenantAsync(tenantB, CancellationToken.None);
        }
    }

    [PostgresFact]
    public async Task Production_recall_query_preserves_the_full_acl_set_with_an_unforced_plan()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        var root = Path.Combine(Path.GetTempPath(), $"postgres-recall-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var migrationConnectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN");
        await using var store = new PostgresMemoryVectorStore(connectionString);
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var findings = new JsonDocumentStore<MemoryFinding>(Path.Combine(root, "findings.json"));
            var graph = new MemoryGraphService(
                new JsonDocumentStore<MemoryGraphNode>(Path.Combine(root, "nodes.json")),
                new JsonDocumentStore<MemoryGraphEdge>(Path.Combine(root, "edges.json")));
            var capturing = new CapturingVectorStore(store);
            var index = new MemoryIndexService(claims, capturing, findings, graph, TimeProvider.System);
            var allowedIds = Enumerable.Range(0, MemoryVectorFormat.MaximumSearchResults).Select(i => $"allowed-{i}").ToArray();
            foreach (var (claimId, ordinal) in allowedIds.Select((id, i) => (id, i)))
                await claims.PutAsync(claimId, new MemoryClaim(claimId, tenantId.ToString("D"), "organization",
                    $"Synthetic launch checkpoint {ordinal}", "source:synthetic", 0.9, "trusted", "Active", 1,
                    Author: "Owner", Verified: true, CreatedAt: DateTimeOffset.UtcNow), CancellationToken.None);
            await claims.PutAsync("private-bob", new MemoryClaim("private-bob", tenantId.ToString("D"), "personal",
                "Synthetic launch private note", "source:synthetic", 0.9, "trusted", "Active", 1,
                Author: "Bob", Verified: true, CreatedAt: DateTimeOffset.UtcNow), CancellationToken.None);
            await claims.PutAsync("foreign", new MemoryClaim("foreign", foreignTenantId.ToString("D"), "organization",
                "Synthetic launch foreign note", "source:synthetic", 0.9, "trusted", "Active", 1,
                Author: "Owner", Verified: true, CreatedAt: DateTimeOffset.UtcNow), CancellationToken.None);
            await index.IndexClaimAsync((await claims.GetAsync("foreign", CancellationToken.None))!, CancellationToken.None);

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT set_config('app.tenant_id', @tenant_id::text, true);
                    INSERT INTO "MemoryEmbeddings" ("ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector", "IndexedAt")
                    SELECT 'synthetic-bulk-' || series, @tenant_id, 'synthetic-hash', 'synthetic-v1',
                           array_to_vector((
                               SELECT array_agg(
                                   CASE
                                       WHEN component = 1 THEN ((series % 17) - 8)::real / 17
                                       WHEN component = 2 THEN ((series % 13) - 6)::real / 13
                                       ELSE ((series + component) % 29)::real / 29
                                   END ORDER BY component)
                               FROM generate_series(1, 256) AS component
                           ), 256, true), now()
                    FROM generate_series(1, 300) AS series;
                    """;
                command.Parameters.AddWithValue("tenant_id", tenantId);
                await command.ExecuteNonQueryAsync();
            }
            if (!string.IsNullOrWhiteSpace(migrationConnectionString))
            {
                await using var migrationConnection = new NpgsqlConnection(migrationConnectionString);
                await migrationConnection.OpenAsync();
                await using var analyzeCommand = migrationConnection.CreateCommand();
                analyzeCommand.CommandText = "ANALYZE \"MemoryEmbeddings\";";
                await analyzeCommand.ExecuteNonQueryAsync();
            }

            var result = await index.SearchHybridAsync(tenantId.ToString("D"), "Synthetic launch", null, 10,
                "Alice", CancellationToken.None);
            Assert.Equal(MemoryVectorFormat.MaximumSearchResults, result.Considered);
            Assert.NotNull(capturing.LastTenantId);
            Assert.NotNull(capturing.LastQueryVector);
            Assert.NotNull(capturing.LastAllowedClaimIds);
            Assert.Equal(allowedIds.OrderBy(id => id, StringComparer.Ordinal),
                capturing.LastAllowedClaimIds!.OrderBy(id => id, StringComparer.Ordinal));
            Assert.Equal(MemoryVectorFormat.MaximumSearchResults, capturing.LastMaxResults);
            Assert.NotNull(capturing.LastResult);
            Assert.Equal(MemoryVectorFormat.MaximumSearchResults, capturing.LastResult.Count);
            Assert.Equal(allowedIds.OrderBy(id => id, StringComparer.Ordinal),
                capturing.LastResult.Select(record => record.ClaimId).OrderBy(id => id, StringComparer.Ordinal));

            await using var planConnection = new NpgsqlConnection(connectionString);
            await planConnection.OpenAsync();
            await using var planTransaction = await planConnection.BeginTransactionAsync();
            await using (var contextCommand = planConnection.CreateCommand())
            {
                contextCommand.Transaction = planTransaction;
                contextCommand.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, true);";
                contextCommand.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
                await contextCommand.ExecuteNonQueryAsync();
            }
            await using (var settingsCommand = planConnection.CreateCommand())
            {
                settingsCommand.Transaction = planTransaction;
                settingsCommand.CommandText = """
                    SELECT current_setting('enable_seqscan'), current_setting('enable_sort'), current_setting('enable_bitmapscan');
                    """;
                await using var settingsReader = await settingsCommand.ExecuteReaderAsync();
                Assert.True(await settingsReader.ReadAsync());
                Assert.Equal("on", settingsReader.GetString(0));
                Assert.Equal("on", settingsReader.GetString(1));
                Assert.Equal("on", settingsReader.GetString(2));
            }
            await using var explainCommand = PostgresMemoryVectorStore.CreateSearchCommand(planConnection, planTransaction,
                Guid.Parse(capturing.LastTenantId!), capturing.LastQueryVector!, capturing.LastAllowedClaimIds!,
                capturing.LastMaxResults);
            explainCommand.CommandText = "EXPLAIN (COSTS ON) " + explainCommand.CommandText;
            await using (var explainReader = await explainCommand.ExecuteReaderAsync())
            {
                var plan = new List<string>();
                while (await explainReader.ReadAsync()) plan.Add(explainReader.GetString(0));
                Assert.True(plan.Any(line => line.Contains("IX_MemoryEmbeddings_Vector", StringComparison.Ordinal)
                        || line.Contains("PK_MemoryEmbeddings", StringComparison.Ordinal)
                        || line.Contains("Seq Scan", StringComparison.Ordinal)),
                    string.Join(Environment.NewLine, plan));
                Assert.Contains(plan, line => line.Contains("ANY", StringComparison.Ordinal));
            }
            await planTransaction.RollbackAsync();
        }
        finally
        {
            await store.DeleteTenantAsync(tenantId.ToString("D"), CancellationToken.None);
            await store.DeleteTenantAsync(foreignTenantId.ToString("D"), CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(migrationConnectionString))
            {
                await using var migrationConnection = new NpgsqlConnection(migrationConnectionString);
                await migrationConnection.OpenAsync();
                await using var analyzeCommand = migrationConnection.CreateCommand();
                analyzeCommand.CommandText = "ANALYZE \"MemoryEmbeddings\";";
                await analyzeCommand.ExecuteNonQueryAsync();
            }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task AssertRlsAndSchemaAsync(NpgsqlDataSource dataSource, string tenantA, string tenantB,
        string claimId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT c.relrowsecurity, c.relforcerowsecurity
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.True(reader.GetBoolean(1));
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM \"MemoryEmbeddings\";";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT format_type(a.atttypid, a.atttypmod), a.attnotnull
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = current_schema() AND c.relname = 'MemoryEmbeddings' AND a.attname = 'Vector';
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("vector(256)", reader.GetString(0));
            Assert.True(reader.GetBoolean(1));
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT has_table_privilege(current_user, 'public."MemoryEmbeddings"', 'SELECT')
                   AND has_table_privilege(current_user, 'public."MemoryEmbeddings"', 'INSERT')
                   AND has_table_privilege(current_user, 'public."MemoryEmbeddings"', 'UPDATE')
                   AND has_table_privilege(current_user, 'public."MemoryEmbeddings"', 'DELETE');
                """;
            Assert.Equal(true, await command.ExecuteScalarAsync());
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SAVEPOINT missing_tenant;";
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                INSERT INTO "MemoryEmbeddings" ("ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector", "IndexedAt")
                VALUES ('missing-tenant', @tenant_id, 'hash', 'test', CAST(@vector AS vector), now());
                """;
            command.Parameters.AddWithValue("tenant_id", Guid.Parse(tenantA));
            command.Parameters.AddWithValue("vector", PostgresVectorLiteral.Serialize(Axis(0)));
            var error = await Record.ExceptionAsync(command.ExecuteNonQueryAsync);
            Assert.IsType<PostgresException>(error);
            Assert.Equal("42501", ((PostgresException)error!).SqlState);
            command.CommandText = "ROLLBACK TO SAVEPOINT missing_tenant;";
            await command.ExecuteNonQueryAsync();
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, true);";
            command.Parameters.Add(new NpgsqlParameter("tenant_id", NpgsqlTypes.NpgsqlDbType.Text) { Value = tenantB });
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT vector_dims(\"Vector\") FROM \"MemoryEmbeddings\" WHERE \"ClaimId\" = @claim_id;";
            command.Parameters.AddWithValue("claim_id", claimId);
            Assert.Equal(256, Convert.ToInt32(await command.ExecuteScalarAsync()));
            command.CommandText = """
                UPDATE "MemoryEmbeddings" SET "ContentHash" = 'cross-tenant-update'
                WHERE "TenantId" = @tenant_a AND "ClaimId" = @foreign_claim;
                """;
            command.Parameters.AddWithValue("tenant_a", Guid.Parse(tenantA));
            command.Parameters.AddWithValue("foreign_claim", "orthogonal");
            Assert.Equal(0, await command.ExecuteNonQueryAsync());
            command.CommandText = """
                DELETE FROM "MemoryEmbeddings" WHERE "TenantId" = @tenant_a AND "ClaimId" = @foreign_claim;
                """;
            Assert.Equal(0, await command.ExecuteNonQueryAsync());
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO "MemoryEmbeddings" ("ClaimId", "TenantId", "ContentHash", "ModelVersion", "Vector", "IndexedAt")
                VALUES ('cross-tenant', @tenant_a, 'hash', 'test', CAST(@vector AS vector), now());
                """;
            command.Parameters.AddWithValue("tenant_a", Guid.Parse(tenantA));
            command.Parameters.AddWithValue("vector", PostgresVectorLiteral.Serialize(Axis(0)));
            var error = await Record.ExceptionAsync(command.ExecuteNonQueryAsync);
            Assert.IsType<PostgresException>(error);
            Assert.Equal("42501", ((PostgresException)error!).SqlState);
        }
        await transaction.RollbackAsync();
    }

    private sealed class CapturingVectorStore(IMemoryVectorStore inner) : IMemoryVectorStore
    {
        public string? LastTenantId { get; private set; }
        public float[]? LastQueryVector { get; private set; }
        public IReadOnlyList<string>? LastAllowedClaimIds { get; private set; }
        public IReadOnlyList<MemoryVectorRecord>? LastResult { get; private set; }
        public int LastMaxResults { get; private set; }

        public Task<MemoryVectorRecord?> GetAsync(string tenantId, string claimId, CancellationToken cancellationToken) =>
            inner.GetAsync(tenantId, claimId, cancellationToken);
        public Task PutAsync(MemoryVectorRecord record, CancellationToken cancellationToken) => inner.PutAsync(record, cancellationToken);
        public Task DeleteAsync(string tenantId, string claimId, CancellationToken cancellationToken) =>
            inner.DeleteAsync(tenantId, claimId, cancellationToken);
        public Task<IReadOnlyList<MemoryVectorRecord>> GetAllAsync(string tenantId, CancellationToken cancellationToken) =>
            inner.GetAllAsync(tenantId, cancellationToken);
        public Task<int> DeleteTenantAsync(string tenantId, CancellationToken cancellationToken) =>
            inner.DeleteTenantAsync(tenantId, cancellationToken);

        public async Task<IReadOnlyList<MemoryVectorRecord>> SearchAsync(string tenantId, float[] queryVector,
            IReadOnlyList<string> allowedClaimIds, int maxResults, CancellationToken cancellationToken)
        {
            LastTenantId = tenantId;
            LastQueryVector = queryVector;
            LastAllowedClaimIds = allowedClaimIds.ToArray();
            LastMaxResults = maxResults;
            LastResult = await inner.SearchAsync(tenantId, queryVector, allowedClaimIds, maxResults, cancellationToken);
            return LastResult;
        }
    }

    private static MemoryVectorRecord VectorRecord(string claimId, string tenantId, float[] vector) =>
        new(claimId, tenantId, "synthetic-hash", "synthetic-v1", vector, DateTimeOffset.UnixEpoch);

    private static float[] Axis(int axis)
    {
        var vector = new float[MemoryVectorFormat.Dimensions];
        vector[axis] = 1;
        return vector;
    }

    private static float[] NegativeAxis(int axis)
    {
        var vector = Axis(axis);
        vector[axis] = -1;
        return vector;
    }
}
