using Npgsql;
using RegentAgent.ControlPlane.Data;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PostgresLTreeTests
{
    [PostgresFact]
    public async Task Postgres_ltree_supports_tenant_scoped_subtrees_depth_and_rls()
    {
        var connectionString = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_DSN")!;
        await PostgresTestSupport.AssertRuntimeRoleAsync(connectionString);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();
        var otherRootId = Guid.NewGuid();
        var rootPath = PositionPath.Root(rootId);
        try
        {
            await SetTenantAsync(connection, transaction, tenantA);
            await InsertPositionAsync(connection, transaction, tenantA, rootId, null, rootPath);
            await InsertPositionAsync(connection, transaction, tenantA, childId, rootId,
                PositionPath.Child(rootPath, rootId, childId));
            await InsertPositionAsync(connection, transaction, tenantA, grandchildId, childId,
                PositionPath.Child(PositionPath.Child(rootPath, rootId, childId), childId, grandchildId));
            await SetTenantAsync(connection, transaction, tenantB);
            await InsertPositionAsync(connection, transaction, tenantB, otherRootId, null,
                PositionPath.Root(otherRootId));
            await SetTenantAsync(connection, transaction, tenantA);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT "Id", nlevel("Path")
                    FROM "Positions"
                    WHERE "TenantId" = @tenant_id AND "Path" <@ CAST(@root_path AS ltree)
                    ORDER BY nlevel("Path"), "Id";
                    """;
                command.Parameters.AddWithValue("tenant_id", tenantA);
                command.Parameters.AddWithValue("root_path", rootPath);
                await using var reader = await command.ExecuteReaderAsync();
                var ids = new List<Guid>();
                while (await reader.ReadAsync())
                {
                    ids.Add(reader.GetGuid(0));
                    if (reader.GetGuid(0) == grandchildId) Assert.Equal(4, reader.GetInt32(1));
                }
                Assert.Equal([rootId, childId, grandchildId], ids);
            }
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    SELECT am.amname, pg_get_indexdef(i.indexrelid)
                    FROM pg_index i
                    JOIN pg_class c ON c.oid = i.indrelid
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    JOIN pg_class ic ON ic.oid = i.indexrelid
                    JOIN pg_am am ON am.oid = ic.relam
                    WHERE n.nspname = current_schema() AND c.relname = 'Positions'
                      AND ic.relname = 'IX_Positions_Path';
                    """;
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("gist", reader.GetString(0));
                Assert.Contains("USING gist (\"Path\")", reader.GetString(1), StringComparison.Ordinal);
            }
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE "Positions" SET "Name" = 'Cross tenant'
                    WHERE "TenantId" = @tenant_b AND "Id" = @foreign_id;
                    """;
                command.Parameters.AddWithValue("tenant_b", tenantB);
                command.Parameters.AddWithValue("foreign_id", otherRootId);
                Assert.Equal(0, await command.ExecuteNonQueryAsync());
                command.CommandText = "DELETE FROM \"Positions\" WHERE \"TenantId\" = @tenant_b AND \"Id\" = @foreign_id;";
                Assert.Equal(0, await command.ExecuteNonQueryAsync());
            }
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO "Positions" ("Id", "TenantId", "Name", "Responsibilities", "CapabilitiesJson", "BudgetMinor", "ParentPositionId", "Path", "Status", "Version")
                    VALUES (@id, @tenant_id, 'Synthetic', 'Synthetic', '[]', 0, NULL, CAST(@path AS ltree), 'Open', 1);
                    """;
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("tenant_id", tenantB);
                command.Parameters.AddWithValue("path", PositionPath.Root(Guid.NewGuid()));
                var error = await Record.ExceptionAsync(command.ExecuteNonQueryAsync);
                Assert.IsType<PostgresException>(error);
                Assert.Equal("42501", ((PostgresException)error!).SqlState);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task SetTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, true);";
        command.Parameters.AddWithValue("tenant_id", tenantId.ToString("D"));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPositionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid id, Guid? parentId, string path)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO "Positions" ("Id", "TenantId", "Name", "Responsibilities", "CapabilitiesJson", "BudgetMinor", "ParentPositionId", "Path", "Status", "Version")
            VALUES (@id, @tenant_id, 'Synthetic', 'Synthetic', '[]', 0, @parent_id, CAST(@path AS ltree), 'Open', 1);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("parent_id", (object?)parentId ?? DBNull.Value);
        command.Parameters.AddWithValue("path", path);
        await command.ExecuteNonQueryAsync();
    }
}
