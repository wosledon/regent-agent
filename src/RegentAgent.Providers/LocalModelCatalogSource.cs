using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Models;

namespace RegentAgent.Providers;

public sealed class LocalModelCatalogSource(TimeProvider? clock = null) : IModelCatalogSource
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    public string SourceId => "local-deterministic-catalog";

    public Task<ModelCatalogSnapshot> FetchAsync(DateOnly snapshotDate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (snapshot, entries) = LocalModelCatalog.CreateSnapshot(snapshotDate);
        var raw = JsonSerializer.Serialize(entries);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        var fetchedAt = clock.GetUtcNow();
        snapshot = snapshot with { RawJson = raw, Checksum = checksum, FetchedAt = fetchedAt };
        return Task.FromResult(new ModelCatalogSnapshot(snapshot, entries, raw, checksum, fetchedAt));
    }
}
