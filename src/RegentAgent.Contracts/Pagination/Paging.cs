namespace RegentAgent.Contracts.Pagination;

public sealed record PageRequest(int Limit = 50, string? Cursor = null)
{
    public int NormalizedLimit => Math.Clamp(Limit, 1, 200);
}

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, DateTimeOffset SnapshotAt);
