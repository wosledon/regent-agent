using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using RegentAgent.ControlPlane.Infrastructure;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("Postgres migrations")]
public sealed class PostgresMigrationRunnerTests
{
    [PostgresMigrationFact]
    public async Task Runner_bootstraps_empty_database_and_enforces_checksum_history()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        try
        {
            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT to_regclass('\"SchemaVersions\"') IS NULL;";
                Assert.Equal(true, await command.ExecuteScalarAsync());
            }

            var first = await RunRunnerAsync(database.TestConnectionString);
            Assert.Equal(0, first.ExitCode);
            Assert.Contains("controlplane-003.sql", first.Output, StringComparison.Ordinal);
            Assert.Contains("controlplane-009.sql", first.Output, StringComparison.Ordinal);

            var expected = PostgresMigrationChecksumManifest.Required.ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT \"Version\", \"Checksum\" FROM \"SchemaVersions\" ORDER BY \"Version\";";
                await using var reader = await command.ExecuteReaderAsync();
                var actual = new Dictionary<int, string>();
                while (await reader.ReadAsync()) actual.Add(reader.GetInt32(0), reader.GetString(1));
                Assert.Equal(expected.Keys.Order().ToArray(), actual.Keys.Order().ToArray());
                foreach (var (version, checksum) in expected) Assert.Equal(checksum, actual[version]);
            }

            var second = await RunRunnerAsync(database.TestConnectionString);
            Assert.Equal(0, second.ExitCode);
            Assert.Contains("SKIP: controlplane-003.sql", second.Output, StringComparison.Ordinal);
            Assert.Contains("SKIP: controlplane-009.sql", second.Output, StringComparison.Ordinal);

            var concurrent = await Task.WhenAll(
                RunRunnerAsync(database.TestConnectionString),
                RunRunnerAsync(database.TestConnectionString));
            Assert.All(concurrent, result => Assert.Equal(0, result.ExitCode));

            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE \"SchemaVersions\" SET \"Checksum\" = NULL WHERE \"Version\" BETWEEN 3 AND 6;";
                await command.ExecuteNonQueryAsync();
            }
            var legacy = await RunRunnerAsync(database.TestConnectionString);
            Assert.Equal(0, legacy.ExitCode);
            Assert.Contains("CHECKSUMS: backfilled NULL SHA-256 values", legacy.Output, StringComparison.Ordinal);

            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE \"SchemaVersions\" SET \"Checksum\" = NULL WHERE \"Version\" = 7;";
                await command.ExecuteNonQueryAsync();
            }
            var missing = await RunRunnerAsync(database.TestConnectionString);
            Assert.Equal(0, missing.ExitCode);

            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE \"SchemaVersions\" SET \"Checksum\" = 'drift' WHERE \"Version\" = 7;";
                await command.ExecuteNonQueryAsync();
            }
            var drift = await RunRunnerAsync(database.TestConnectionString);
            Assert.NotEqual(0, drift.ExitCode);
            Assert.Contains("checksum drift", drift.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationFact]
    public async Task Runner_rejects_tampered_sql_before_creating_schema()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        var sqlDirectory = Path.Combine(Path.GetTempPath(), $"regent-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sqlDirectory);
        try
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql")))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("controlplane-", StringComparison.Ordinal) &&
                    (name.EndsWith(".sql", StringComparison.Ordinal) || name == "controlplane-checksums.json"))
                    File.Copy(path, Path.Combine(sqlDirectory, name));
            }
            var tampered = Path.Combine(sqlDirectory, "controlplane-003.sql");
            File.AppendAllText(tampered, "\n-- tampered after manifest generation\n");

            var result = await RunRunnerAsync(database.TestConnectionString, "--sql-dir", sqlDirectory);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("does not match the checksum manifest", result.Output, StringComparison.Ordinal);
            await using var connection = new NpgsqlConnection(database.TestConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('\"SchemaVersions\"') IS NULL;";
            Assert.Equal(true, await command.ExecuteScalarAsync());
        }
        finally
        {
            if (Directory.Exists(sqlDirectory)) Directory.Delete(sqlDirectory, recursive: true);
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationFact]
    public async Task Runner_waits_on_legacy_per_migration_lock_before_reading_history()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        try
        {
            await using var legacyLock = new NpgsqlConnection(database.TestConnectionString);
            await legacyLock.OpenAsync();
            await using var lockCommand = legacyLock.CreateCommand();
            lockCommand.CommandText = $"SELECT pg_advisory_lock({6_317_003});";
            await lockCommand.ExecuteNonQueryAsync();

            var result = await RunRunnerAsync(database.TestConnectionString);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("lock timeout", result.Output, StringComparison.OrdinalIgnoreCase);
            await using var connection = new NpgsqlConnection(database.TestConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('\"SchemaVersions\"') IS NULL;";
            Assert.Equal(true, await command.ExecuteScalarAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationFact]
    public async Task Legacy_runner_lock_conflicts_with_new_runner_lock_key()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        try
        {
            await using var newRunnerLock = new NpgsqlConnection(database.TestConnectionString);
            await newRunnerLock.OpenAsync();
            await using (var lockCommand = newRunnerLock.CreateCommand())
            {
                lockCommand.CommandText = $"SELECT pg_advisory_lock({6_317_003});";
                await lockCommand.ExecuteNonQueryAsync();
            }

            await using var legacyRunner = new NpgsqlConnection(database.TestConnectionString);
            await legacyRunner.OpenAsync();
            await using var legacyCommand = legacyRunner.CreateCommand();
            legacyCommand.CommandText = $"SET lock_timeout = '100ms'; SELECT pg_advisory_lock({6_317_003});";
            var error = await Assert.ThrowsAsync<PostgresException>(() => legacyCommand.ExecuteNonQueryAsync());

            Assert.Equal("55P03", error.SqlState);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationTheory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Runner_upgrades_pre_checksum_schemas_three_through_seven_to_schema_nine(int version)
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        try
        {
            await ApplyMigrationsAsync(database.TestConnectionString, 3, version);
            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT NOT EXISTS (
                        SELECT 1 FROM pg_attribute a
                        JOIN pg_class c ON c.oid = a.attrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = current_schema() AND c.relname = 'SchemaVersions'
                          AND a.attname = 'Checksum' AND a.attnum > 0 AND NOT a.attisdropped
                    );
                    """;
                Assert.Equal(true, await command.ExecuteScalarAsync());
            }

            var result = await RunRunnerAsync(database.TestConnectionString);

            Assert.Equal(0, result.ExitCode);
            await using var verification = new NpgsqlConnection(database.TestConnectionString);
            await verification.OpenAsync();
            await using var verifyCommand = verification.CreateCommand();
            verifyCommand.CommandText = "SELECT \"Version\", \"Checksum\" FROM \"SchemaVersions\" ORDER BY \"Version\";";
            await using var reader = await verifyCommand.ExecuteReaderAsync();
            var actual = new Dictionary<int, string>();
            while (await reader.ReadAsync()) actual.Add(reader.GetInt32(0), reader.GetString(1));
            Assert.Equal(PostgresMigrationChecksumManifest.Required.Keys.Order().ToArray(), actual.Keys.Order().ToArray());
            foreach (var (requiredVersion, checksum) in PostgresMigrationChecksumManifest.Required)
                Assert.Equal(checksum, actual[requiredVersion]);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationFact]
    public async Task Runner_recovers_interrupted_schema_eight_checksum_backfill()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        try
        {
            await ApplyMigrationsAsync(database.TestConnectionString, 3, 8);
            await using (var connection = new NpgsqlConnection(database.TestConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM \"SchemaVersions\" WHERE \"Checksum\" IS NOT NULL;";
                Assert.Equal(0L, await command.ExecuteScalarAsync());
            }

            var result = await RunRunnerAsync(database.TestConnectionString);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("CHECKSUMS: backfilled NULL SHA-256 values", result.Output, StringComparison.Ordinal);
            await using var verification = new NpgsqlConnection(database.TestConnectionString);
            await verification.OpenAsync();
            await using var verifyCommand = verification.CreateCommand();
            verifyCommand.CommandText = "SELECT bool_and(\"Checksum\" IS NOT NULL) FROM \"SchemaVersions\";";
            Assert.Equal(true, await verifyCommand.ExecuteScalarAsync());
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [PostgresMigrationFact]
    public async Task Runner_rejects_newest_migration_that_did_not_record_its_version()
    {
        var dsn = Environment.GetEnvironmentVariable("REGENT_TEST_POSTGRES_MIGRATION_DSN")!;
        var database = await CreateDatabaseAsync(dsn);
        var sqlDirectory = Path.Combine(Path.GetTempPath(), $"regent-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sqlDirectory);
        try
        {
            File.WriteAllText(Path.Combine(sqlDirectory, "controlplane-003.sql"), """
                BEGIN;
                CREATE TABLE "SchemaVersions" (
                    "Version" integer PRIMARY KEY,
                    "Name" text NOT NULL,
                    "AppliedAt" timestamp with time zone NOT NULL
                );
                INSERT INTO "SchemaVersions" ("Version", "Name", "AppliedAt")
                VALUES (3, 'synthetic-three', now());
                COMMIT;
                """);
            File.WriteAllText(Path.Combine(sqlDirectory, "controlplane-004.sql"), """
                BEGIN;
                CREATE TABLE "SyntheticFour" ("Id" integer PRIMARY KEY);
                COMMIT;
                """);
            WriteChecksumManifest(sqlDirectory, 3, 4);

            var result = await RunRunnerAsync(database.TestConnectionString, "--sql-dir", sqlDirectory);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(result.Output.Contains("Newest migration 3 did not record schema version 4", StringComparison.Ordinal),
                result.Output);
        }
        finally
        {
            if (Directory.Exists(sqlDirectory)) Directory.Delete(sqlDirectory, recursive: true);
            await DropDatabaseAsync(database);
        }
    }

    private static async Task<(string AdminConnectionString, string TestConnectionString, string DatabaseName)>
        CreateDatabaseAsync(string dsn)
    {
        var databaseName = $"regent_runner_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(dsn);
        await ExecuteAdminAsync(adminBuilder.ConnectionString, $"CREATE DATABASE \"{databaseName}\";");
        var testBuilder = new NpgsqlConnectionStringBuilder(dsn)
        {
            Database = databaseName,
            SearchPath = "public"
        };
        return (adminBuilder.ConnectionString, testBuilder.ConnectionString, databaseName);
    }

    private static async Task DropDatabaseAsync(
        (string AdminConnectionString, string TestConnectionString, string DatabaseName) database)
    {
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync(database.AdminConnectionString,
            $"DROP DATABASE IF EXISTS \"{database.DatabaseName}\" WITH (FORCE);");
    }

    private static async Task ApplyMigrationsAsync(string connectionString, int firstVersion, int lastVersion)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        for (var version = firstVersion; version <= lastVersion; version++)
        {
            var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql", $"controlplane-{version:000}.sql");
            await using var command = connection.CreateCommand();
            command.CommandText = await File.ReadAllTextAsync(path);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static void WriteChecksumManifest(string sqlDirectory, params int[] versions)
    {
        var migrations = versions.Select(version =>
        {
            var fileName = $"controlplane-{version:000}.sql";
            var checksum = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(sqlDirectory, fileName)))).ToLowerInvariant();
            return new { version, file = fileName, sha256 = checksum };
        }).ToArray();
        var manifest = new
        {
            schema = "regent-controlplane-migration-checksums/v1",
            migrations
        };
        File.WriteAllText(
            Path.Combine(sqlDirectory, "controlplane-checksums.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static async Task<(int ExitCode, string Output)> RunRunnerAsync(string connectionString,
        params string[] additionalArguments)
    {
        var startInfo = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(PostgresTestSupport.RepositoryRoot, "scripts", "migrate-postgres.py"));
        startInfo.ArgumentList.Add("--database-url");
        startInfo.ArgumentList.Add(connectionString);
        foreach (var argument in additionalArguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("python3 could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task ExecuteAdminAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
