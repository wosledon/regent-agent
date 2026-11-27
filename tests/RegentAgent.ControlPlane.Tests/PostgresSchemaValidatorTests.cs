using System.Security.Cryptography;
using RegentAgent.ControlPlane.Infrastructure;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PostgresSchemaValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Older_schema_versions_fail_even_when_vector_objects_exist(int version)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            PostgresSchemaValidator.Validate(version, ValidSchema()));

        Assert.Contains("schema version 9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_schema_nine_passes()
    {
        PostgresSchemaValidator.Validate(9, ValidSchema());
    }

    [Fact]
    public void Embedded_checksum_manifest_matches_checked_in_sql_files()
    {
        Assert.Equal(Enumerable.Range(3, 7), PostgresMigrationChecksumManifest.Required.Keys.Order());
        foreach (var (version, checksum) in PostgresMigrationChecksumManifest.Required)
        {
            var path = Path.Combine(PostgresTestSupport.RepositoryRoot, "deploy", "sql",
                $"controlplane-{version:000}.sql");
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            Assert.Equal(checksum, actual);
        }
    }

    public static TheoryData<string, bool[]> IncompleteSchemas()
    {
        var names = new[]
        {
            nameof(PostgresSchemaSnapshot.VectorExtension), nameof(PostgresSchemaSnapshot.LTreeExtension),
            nameof(PostgresSchemaSnapshot.MemoryEmbeddingsTable), nameof(PostgresSchemaSnapshot.Vector256NotNull),
            nameof(PostgresSchemaSnapshot.HnswCosineIndex), nameof(PostgresSchemaSnapshot.PositionsPathLtreeNotNull),
            nameof(PostgresSchemaSnapshot.PositionsPathGistIndex), nameof(PostgresSchemaSnapshot.MemoryRlsEnabledAndForced),
            nameof(PostgresSchemaSnapshot.MemoryTenantPolicy), nameof(PostgresSchemaSnapshot.PositionsRlsEnabledAndForced),
            nameof(PostgresSchemaSnapshot.PositionsTenantPolicy), nameof(PostgresSchemaSnapshot.ApprovalsRlsEnabledAndForced),
            nameof(PostgresSchemaSnapshot.ApprovalsTenantPolicy), nameof(PostgresSchemaSnapshot.ApprovalAuthorityColumns),
            nameof(PostgresSchemaSnapshot.OutboxRetryColumns), nameof(PostgresSchemaSnapshot.SchemaVersionsChecksumColumn),
            nameof(PostgresSchemaSnapshot.RequiredChecksumHistory),
            nameof(PostgresSchemaSnapshot.MilestoneTenantIdColumn),
            nameof(PostgresSchemaSnapshot.MilestoneProjectForeignKey),
            nameof(PostgresSchemaSnapshot.ProjectsTenantIdUniqueIndex),
            nameof(PostgresSchemaSnapshot.MilestoneTenantProjectIndex),
            nameof(PostgresSchemaSnapshot.MilestoneTenantDerivationTrigger)
        };
        var data = new TheoryData<string, bool[]>();
        for (var i = 0; i < names.Length; i++)
        {
            var flags = Enumerable.Repeat(true, names.Length).ToArray();
            flags[i] = false;
            data.Add(names[i], flags);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(IncompleteSchemas))]
    public void Incomplete_schema_nine_fails_closed(string missingProperty, bool[] flags)
    {
        var incomplete = new PostgresSchemaSnapshot(flags[0], flags[1], flags[2], flags[3], flags[4], flags[5],
            flags[6], flags[7], flags[8], flags[9], flags[10], flags[11], flags[12], flags[13], flags[14],
            flags[15], flags[16], flags[17], flags[18], flags[19], flags[20], flags[21]);
        var error = Assert.Throws<InvalidOperationException>(() => PostgresSchemaValidator.Validate(9, incomplete));

        Assert.NotEmpty(missingProperty);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static PostgresSchemaSnapshot ValidSchema() => new(
        true, true, true, true, true, true, true, true, true, true, true, true, true, true, true,
        true, true, true, true, true, true, true);
}
