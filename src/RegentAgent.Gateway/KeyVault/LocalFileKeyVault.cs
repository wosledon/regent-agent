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
            var record = KeyEnvelope.Encrypt(masterKey, tenantId, provider, model, apiKey, 1);
            records.Add(record);
            await SaveAsync(records, cancellationToken);
            return EncryptedKeyRecordMapper.ToInfo(record);
        }
        finally { gate.Release(); }
    }

    public async Task<KeyVersionInfo> RotateAsync(string tenantId, string keyRef, string apiKey, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var records = await LoadAsync(cancellationToken);
            var current = records.SingleOrDefault(x => x.TenantId == tenantId && x.KeyRef == keyRef)
                ?? throw new KeyNotFoundException("Key reference not found.");
            current.State = "Retired";
            var next = KeyEnvelope.Encrypt(masterKey, current.TenantId, current.Provider, current.Model, apiKey,
                current.Version + 1, current.KeyRef);
            records.Add(next);
            await SaveAsync(records, cancellationToken);
            return EncryptedKeyRecordMapper.ToInfo(next);
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
        try { return (await LoadAsync(cancellationToken)).Where(x => x.TenantId == tenantId).Select(EncryptedKeyRecordMapper.ToInfo).ToArray(); }
        finally { gate.Release(); }
    }

    private async Task<List<EncryptedKeyRecord>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<List<EncryptedKeyRecord>>(json) ?? [];
    }

    private async Task SaveAsync(List<EncryptedKeyRecord> records, CancellationToken cancellationToken)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(records), cancellationToken);
        File.Move(temp, path, true);
    }
}
