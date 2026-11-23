using System.Text.Json;

namespace RegentAgent.Storage;

public sealed class JsonDocumentStore<T> : ITenantJsonDocumentStore where T : class
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    public JsonDocumentStore(string path)
    {
        this.path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await LoadAsync(cancellationToken)).Values.ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<T?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return (await LoadAsync(cancellationToken)).GetValueOrDefault(id); }
        finally { gate.Release(); }
    }

    public async Task PutAsync(string id, T value, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            values[id] = value;
            await SaveAsync(values, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            if (!values.Remove(id)) return false;
            await SaveAsync(values, cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<int> DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            var keys = values.Where(x => BelongsToTenant(x.Key, x.Value, tenantId)).Select(x => x.Key).ToArray();
            foreach (var key in keys) values.Remove(key);
            if (keys.Length > 0) await SaveAsync(values, cancellationToken);
            return keys.Length;
        }
        finally { gate.Release(); }
    }

    private static bool BelongsToTenant<TValue>(string key, TValue value, Guid tenantId)
    {
        if (key.Equals(tenantId.ToString(), StringComparison.OrdinalIgnoreCase)
            || key.StartsWith($"{tenantId}:", StringComparison.OrdinalIgnoreCase)) return true;
        var raw = typeof(TValue).GetProperty("TenantId")?.GetValue(value);
        return raw switch
        {
            Guid id => id == tenantId,
            string text => Guid.TryParse(text, out var id) && id == tenantId,
            _ => false
        };
    }

    private async Task<Dictionary<string, T>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<Dictionary<string, T>>(json) ?? new(StringComparer.Ordinal);
    }

    private async Task SaveAsync(Dictionary<string, T> values, CancellationToken cancellationToken)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(values), cancellationToken);
        File.Move(temp, path, true);
    }
}
