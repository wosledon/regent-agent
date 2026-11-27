using System.Collections.Concurrent;
using System.Text.Json;

namespace RegentAgent.Storage;

public sealed class JsonDocumentStore<T> : IDocumentStore<T>, ITenantJsonDocumentStore where T : class
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string path;
    private readonly SemaphoreSlim gate;
    private readonly ITenantPurgeStateProvider? purgeState;

    public JsonDocumentStore(string path, ITenantPurgeStateProvider? purgeState = null)
    {
        this.path = Path.GetFullPath(path);
        this.purgeState = purgeState;
        // Different adapters can resolve the same module file; serialize by path, not by wrapper instance.
        gate = Gates.GetOrAdd(this.path, _ => new SemaphoreSlim(1, 1));
        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
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

    public async Task<T> GetOrAddAsync(string id, Func<T> valueFactory, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            if (values.TryGetValue(id, out var existing) && existing is not null) return existing;
            var created = valueFactory();
            using var admission = await EnterWriteAsync(id, created, cancellationToken);
            values[id] = created;
            await SaveAsync(values, cancellationToken);
            return created;
        }
        finally { gate.Release(); }
    }

    public async Task<T> MutateAsync(string id, Func<T?, T> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            var current = values.GetValueOrDefault(id);
            using var admission = await EnterWriteAsync(id, current, cancellationToken);
            var next = mutation(current);
            using var resultAdmission = admission is null ? await EnterWriteAsync(id, next, cancellationToken) : null;
            values[id] = next;
            await SaveAsync(values, cancellationToken);
            return next;
        }
        finally { gate.Release(); }
    }

    public async Task<T> MutateAsync(string id, Func<T?, Task<T>> mutation, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadAsync(cancellationToken);
            var current = values.GetValueOrDefault(id);
            using var admission = await EnterWriteAsync(id, current, cancellationToken);
            var next = await mutation(current);
            using var resultAdmission = admission is null ? await EnterWriteAsync(id, next, cancellationToken) : null;
            values[id] = next;
            await SaveAsync(values, cancellationToken);
            return next;
        }
        finally { gate.Release(); }
    }

    public async Task PutAsync(string id, T value, CancellationToken cancellationToken)
    {
        using var admission = await EnterWriteAsync(id, value, cancellationToken);
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
            if (!values.TryGetValue(id, out var value)) return false;
            using var admission = await EnterWriteAsync(id, value, cancellationToken);
            values.Remove(id);
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

    private Task<IDisposable?> EnterWriteAsync(string id, object? value,
        CancellationToken cancellationToken)
    {
        if (TryResolveTenantId(id, value) is not { } tenantId) return Task.FromResult<IDisposable?>(null);
        return purgeState is null
            ? Task.FromResult<IDisposable?>(TenantPurgeFence.EnterWriteAsync(tenantId, cancellationToken)
                .GetAwaiter().GetResult())
            : EnterWriteWithPurgeStateAsync(tenantId, cancellationToken);
    }

    private async Task<IDisposable?> EnterWriteWithPurgeStateAsync(Guid tenantId,
        CancellationToken cancellationToken)
    {
        await TenantPurgeFence.CheckPersistedPurgeAsync(tenantId, cancellationToken, purgeState);
        return await TenantPurgeFence.EnterWriteAsync(tenantId, cancellationToken);
    }

    private static Guid? TryResolveTenantId(string key, object? value)
    {
        var valueTenantId = value?.GetType().GetProperty("TenantId")?.GetValue(value) switch
        {
            Guid id when id != Guid.Empty => id,
            string text when Guid.TryParse(text, out var id) && id != Guid.Empty => id,
            _ => (Guid?)null
        };
        if (valueTenantId is { } tenantId) return tenantId;
        if (Guid.TryParse(key, out var keyTenantId)) return keyTenantId;
        var separator = key.IndexOf(':');
        return separator > 0 && Guid.TryParse(key[..separator], out var prefixedTenantId)
            ? prefixedTenantId : null;
    }

    private static bool BelongsToTenant(string key, T value, Guid tenantId) =>
        TryResolveTenantId(key, value) == tenantId;

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
