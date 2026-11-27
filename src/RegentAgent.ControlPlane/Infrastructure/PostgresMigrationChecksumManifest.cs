using System.Globalization;
using System.Text.Json;

namespace RegentAgent.ControlPlane.Infrastructure;

internal static class PostgresMigrationChecksumManifest
{
    private const string ResourceName =
        "RegentAgent.ControlPlane.Infrastructure.PostgresMigrationChecksums.json";
    private const string ManifestSchema = "regent-controlplane-migration-checksums/v1";

    private static readonly Lazy<IReadOnlyDictionary<int, string>> Checksums = new(Load);

    public static IReadOnlyDictionary<int, string> Required => Checksums.Value;

    public static string GetChecksum(int version) =>
        Required.TryGetValue(version, out var checksum)
            ? checksum
            : throw new InvalidOperationException($"Migration version {version} is absent from the embedded checksum manifest.");

    public static string ChecksumValuesSql => string.Join(", ", Required
        .OrderBy(pair => pair.Key)
        .Select(pair => $"({pair.Key.ToString(CultureInfo.InvariantCulture)}, {SqlLiteral(pair.Value)})"));

    private static IReadOnlyDictionary<int, string> Load()
    {
        using var stream = typeof(PostgresMigrationChecksumManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest is missing.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != ManifestSchema)
            throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest schema is unsupported.");

        var expectedVersions = Enumerable.Range(3, 7).ToArray();
        var migrations = root.GetProperty("migrations").EnumerateArray().ToArray();
        if (migrations.Length != expectedVersions.Length)
            throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest must contain versions 003 through 009.");

        var checksums = new Dictionary<int, string>();
        foreach (var migration in migrations)
        {
            var version = migration.GetProperty("version").GetInt32();
            var expectedFile = $"controlplane-{version:000}.sql";
            var file = migration.GetProperty("file").GetString();
            var checksum = migration.GetProperty("sha256").GetString();
            if (!expectedVersions.Contains(version) || file != expectedFile || checksum is null ||
                checksum.Length != 64 || checksum.Any(character => !Uri.IsHexDigit(character)) ||
                checksum.Any(char.IsUpper))
                throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest is invalid.");
            if (!checksums.TryAdd(version, checksum))
                throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest contains a duplicate version.");
        }

        if (!checksums.Keys.OrderBy(version => version).SequenceEqual(expectedVersions))
            throw new InvalidOperationException("Embedded PostgreSQL migration checksum manifest must contain versions 003 through 009.");
        return checksums;
    }

    private static string SqlLiteral(string value) => "'" + value.Replace("'", "''") + "'";
}
