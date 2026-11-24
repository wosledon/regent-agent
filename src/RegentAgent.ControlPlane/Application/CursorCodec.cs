using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RegentAgent.ControlPlane.Application;

public sealed class CursorCodec(string key)
{
    private readonly byte[] keyBytes = Encoding.UTF8.GetBytes(key);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Encode(Guid tenantId, string filter, DateTimeOffset snapshotAt, string sortKey)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(tenantId, filter, snapshotAt, sortKey), JsonOptions);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(keyBytes, payload));
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_') + "." + signature;
    }

    public string Decode(string cursor, Guid tenantId, string filter)
    {
        var parts = cursor.Split('.', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Pagination cursor is invalid.");
        try
        {
            var payload = Convert.FromBase64String(parts[0].Replace('-', '+').Replace('_', '/')
                + new string('=', (4 - parts[0].Length % 4) % 4));
            var expected = Convert.ToBase64String(HMACSHA256.HashData(keyBytes, payload));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[1])))
                throw new InvalidOperationException("Pagination cursor signature is invalid.");
            var value = JsonSerializer.Deserialize<CursorPayload>(payload, JsonOptions)
                ?? throw new InvalidOperationException("Pagination cursor is invalid.");
            if (value.TenantId != tenantId || value.Filter != filter || string.IsNullOrWhiteSpace(value.SortKey)
                || value.SnapshotAt < DateTimeOffset.UtcNow.AddHours(-1) || value.SnapshotAt > DateTimeOffset.UtcNow.AddMinutes(5))
                throw new InvalidOperationException("Pagination cursor is stale or belongs to another query.");
            return value.SortKey;
        }
        catch (FormatException) { throw new InvalidOperationException("Pagination cursor is invalid."); }
        catch (JsonException) { throw new InvalidOperationException("Pagination cursor is invalid."); }
    }

    public static string Filter(params object?[] values) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values, JsonOptions)))).ToLowerInvariant();

    private sealed record CursorPayload(Guid TenantId, string Filter, DateTimeOffset SnapshotAt, string SortKey);
}
