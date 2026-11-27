namespace RegentAgent.ControlPlane.Data;

public static class PositionPath
{
    public const int MaximumDepth = 4;
    private const string RootLabel = "root";

    public static string Root(Guid positionId)
    {
        if (positionId == Guid.Empty) throw new ArgumentException("Position ID is invalid.", nameof(positionId));
        return $"{RootLabel}.{positionId:N}";
    }

    public static string Child(string parentPath, Guid parentId, Guid childId)
    {
        Validate(parentPath, parentId);
        if (Depth(parentPath) >= MaximumDepth) throw new InvalidOperationException("Organization depth cannot exceed 4.");
        var path = $"{parentPath}.{NonEmptyId(childId):N}";
        Validate(path);
        return path;
    }

    public static string Sibling(string path, Guid positionId, Guid siblingId)
    {
        Validate(path, positionId);
        var labels = path.Split('.');
        var sibling = $"{string.Join('.', labels[..^1])}.{NonEmptyId(siblingId):N}";
        Validate(sibling);
        return sibling;
    }

    public static string Rebase(string subtreePath, string newParentPath, string descendantPath)
    {
        Validate(subtreePath);
        Validate(newParentPath);
        Validate(descendantPath);
        if (descendantPath != subtreePath && !descendantPath.StartsWith($"{subtreePath}.", StringComparison.Ordinal))
            throw new InvalidOperationException("Position is not part of the source subtree.");
        var suffix = descendantPath[subtreePath.Length..];
        var rebased = $"{newParentPath}{suffix}";
        if (rebased.Split('.').Length - 1 > MaximumDepth)
            throw new InvalidOperationException("Organization depth cannot exceed 4.");
        Validate(rebased);
        return rebased;
    }

    public static int Depth(string path)
    {
        Validate(path);
        return path.Split('.').Length - 1;
    }

    public static void Validate(string path, Guid? expectedPositionId = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Position path is missing.");
        var labels = path.Split('.');
        if (labels.Length is < 2 or > MaximumDepth + 1 || labels[0] != RootLabel)
            throw new InvalidOperationException("Position path is invalid.");
        foreach (var label in labels[1..])
        {
            if (!Guid.TryParseExact(label, "N", out var id) || id == Guid.Empty)
                throw new InvalidOperationException("Position path is invalid.");
        }
        if (expectedPositionId is { } expected && labels[^1] != expected.ToString("N"))
            throw new InvalidOperationException("Position path does not match its position.");
    }

    public static IReadOnlyDictionary<Guid, string> BuildPaths(IEnumerable<PositionRecord> positions)
    {
        var items = positions.ToArray();
        var byId = items.GroupBy(position => position.Id).ToDictionary(group => group.Key, group => group.Single());
        var paths = new Dictionary<Guid, string>();
        var visiting = new HashSet<Guid>();

        string Build(PositionRecord position)
        {
            if (paths.TryGetValue(position.Id, out var existing)) return existing;
            if (!visiting.Add(position.Id)) throw new InvalidOperationException("Organization hierarchy contains a cycle.");
            try
            {
                string path;
                if (position.ParentPositionId is not { } parentId)
                {
                    path = Root(position.Id);
                }
                else
                {
                    if (!byId.TryGetValue(parentId, out var parent) || parent.TenantId != position.TenantId)
                        throw new InvalidOperationException("Organization hierarchy contains a missing or cross-tenant parent.");
                    path = Child(Build(parent), parentId, position.Id);
                }
                if (!string.IsNullOrEmpty(position.Path) && position.Path != path)
                    throw new InvalidOperationException("Stored position path does not match its hierarchy.");
                paths[position.Id] = path;
                return path;
            }
            finally
            {
                visiting.Remove(position.Id);
            }
        }

        foreach (var position in items) Build(position);
        return paths;
    }

    private static Guid NonEmptyId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Position ID is invalid.", nameof(id));
        return id;
    }
}
