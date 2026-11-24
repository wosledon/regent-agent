using RegentAgent.Contracts.Models;

namespace RegentAgent.Providers;

public sealed record ModelCatalogSnapshot(
    ModelSnapshot Snapshot,
    IReadOnlyList<ModelCatalogEntry> Entries,
    string RawJson,
    string Checksum,
    DateTimeOffset FetchedAt);

public interface IModelCatalogSource
{
    string SourceId { get; }
    Task<ModelCatalogSnapshot> FetchAsync(DateOnly snapshotDate, CancellationToken cancellationToken);
}

public sealed class ModelCatalogSourceException(string message, int? statusCode = null, bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public int? StatusCode { get; } = statusCode;
    public bool Retryable { get; } = retryable;
}
