namespace RegentAgent.Gateway.KeyVault;

internal sealed class EncryptedKeyRecord
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

internal static class EncryptedKeyRecordMapper
{
    public static KeyVersionInfo ToInfo(EncryptedKeyRecord record) => new(
        record.KeyRef, record.TenantId, record.Provider, record.Model, record.Version, record.State, record.CreatedAt);
}

internal static class KeyEnvelope
{
    public static EncryptedKeyRecord Encrypt(byte[] masterKey, string tenantId, string provider, string model,
        string apiKey, int version, string? keyRef = null)
    {
        var dataKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var (encrypted, nonce, tag) = AesGcmCrypt.Encrypt(dataKey, apiKey);
        var (wrapped, wrapNonce, wrapTag) = AesGcmCrypt.EncryptBytes(masterKey, dataKey);
        return new EncryptedKeyRecord
        {
            KeyRef = keyRef ?? Guid.NewGuid().ToString("N"), TenantId = tenantId, Provider = provider, Model = model,
            Version = version, State = "Active", CreatedAt = DateTimeOffset.UtcNow,
            EncryptedKey = encrypted, KeyNonce = nonce, KeyTag = tag,
            WrappedDataKey = wrapped, WrapNonce = wrapNonce, WrapTag = wrapTag
        };
    }
}
