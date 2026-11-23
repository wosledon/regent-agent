using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RegentAgent.Gateway.KeyVault;

public sealed class LocalFileKeyVault : IKeyVault
{
    private readonly string path;
    private readonly byte[] masterKey;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalFileKeyVault(string path, IMasterKeyProvider masterKeyProvider)
    {
        this.path = path;
        masterKey = masterKeyProvider.GetMasterKey();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task<KeyVersionInfo> SetAsync(string tenantId, string provider, string model, string apiKey, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            var record = Encrypt(tenantId, provider, model, apiKey, 1);
            records.Add(record);
            await SaveAsync(records, cancellationToken);
            return ToInfo(record);
        }
        finally { gate.Release(); }
    }

    public async Task<KeyVersionInfo> RotateAsync(string keyRef, string apiKey, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            var current = records.SingleOrDefault(x => x.KeyRef == keyRef) ?? throw new KeyNotFoundException("Key reference not found.");
            current.State = "Retired";
            var next = Encrypt(current.TenantId, current.Provider, current.Model, apiKey, current.Version + 1);
            next.KeyRef = current.KeyRef;
            records.Add(next);
            await SaveAsync(records, cancellationToken);
            return ToInfo(next);
        }
        finally { gate.Release(); }
    }

    public async Task<string?> UnlockAsync(string tenantId, string keyRef, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var record = (await LoadAsync(cancellationToken)).SingleOrDefault(x => x.TenantId == tenantId && x.KeyRef == keyRef && x.State == "Active");
            if (record is null) return null;
            var dataKey = AesGcmCrypt.Unwrap(masterKey, record.WrappedDataKey, record.WrapNonce, record.WrapTag);
            return AesGcmCrypt.Unlock(dataKey, record.EncryptedKey, record.KeyNonce, record.KeyTag);
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(string tenantId, string keyRef, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            records.RemoveAll(x => x.TenantId == tenantId && x.KeyRef == keyRef);
            await SaveAsync(records, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<KeyVersionInfo>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await LoadAsync(cancellationToken)).Where(x => x.TenantId == tenantId).Select(ToInfo).ToArray(); }
        finally { gate.Release(); }
    }

    private EncryptedRecord Encrypt(string tenantId, string provider, string model, string apiKey, int version)
    {
        var dataKey = RandomNumberGenerator.GetBytes(32);
        var (encrypted, nonce, tag) = AesGcmCrypt.Encrypt(dataKey, apiKey);
        var (wrapped, wrapNonce, wrapTag) = AesGcmCrypt.EncryptBytes(masterKey, dataKey);
        return new EncryptedRecord
        {
            KeyRef = Guid.NewGuid().ToString("N"), TenantId = tenantId, Provider = provider, Model = model,
            Version = version, State = "Active", CreatedAt = DateTimeOffset.UtcNow,
            EncryptedKey = encrypted, KeyNonce = nonce, KeyTag = tag,
            WrappedDataKey = wrapped, WrapNonce = wrapNonce, WrapTag = wrapTag
        };
    }

    private async Task<List<EncryptedRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<List<EncryptedRecord>>(json) ?? [];
    }

    private async Task SaveAsync(List<EncryptedRecord> records, CancellationToken cancellationToken)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(records), cancellationToken);
        File.Move(temp, path, true);
    }

    private static KeyVersionInfo ToInfo(EncryptedRecord record) => new(
        record.KeyRef, record.TenantId, record.Provider, record.Model, record.Version, record.State, record.CreatedAt);

    private sealed record EncryptedRecord
    {
        public string KeyRef { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Version { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string EncryptedKey { get; set; } = string.Empty;
        public string KeyNonce { get; set; } = string.Empty;
        public string KeyTag { get; set; } = string.Empty;
        public string WrappedDataKey { get; set; } = string.Empty;
        public string WrapNonce { get; set; } = string.Empty;
        public string WrapTag { get; set; } = string.Empty;
    }
}

internal static class AesGcmCrypt
{
    public static (string Cipher, string Nonce, string Tag) Encrypt(byte[] key, string value) =>
        EncryptBytes(key, Encoding.UTF8.GetBytes(value));

    public static (string Cipher, string Nonce, string Tag) EncryptBytes(byte[] key, byte[] value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[value.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, value, cipher, tag);
        return (Convert.ToBase64String(cipher), Convert.ToBase64String(nonce), Convert.ToBase64String(tag));
    }

    public static byte[] Unwrap(byte[] key, string cipher, string nonce, string tag)
    {
        var plain = new byte[Convert.FromBase64String(cipher).Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(Convert.FromBase64String(nonce), Convert.FromBase64String(cipher), Convert.FromBase64String(tag), plain);
        return plain;
    }

    public static string Unlock(byte[] key, string cipher, string nonce, string tag) =>
        Encoding.UTF8.GetString(Unwrap(key, cipher, nonce, tag));
}
