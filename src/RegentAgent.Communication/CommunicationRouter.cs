namespace RegentAgent.Communication;

public sealed record RouteNode(string Id, string? ParentId, IReadOnlyList<string> Labels);
public sealed record RouteResult(string RouteType, IReadOnlyList<string> Path, string Reason);

public sealed class CommunicationRouter
{
    public RouteResult ManagementRoute(RouteNode from, IReadOnlyList<RouteNode> nodes)
    {
        var path = new List<string> { from.Id };
        var current = from;
        while (current.ParentId is { } parent)
        {
            var next = nodes.SingleOrDefault(x => x.Id == parent);
            if (next is null) break;
            path.Add(next.Id);
            current = next;
        }
        return new RouteResult("Management", path, "Follow reporting line.");
    }

    public RouteResult ProfessionalRoute(RouteNode from, string label, IReadOnlyList<RouteNode> nodes)
    {
        var path = new List<string> { from.Id };
        var current = from;
        while (current.ParentId is { } parent)
        {
            var next = nodes.SingleOrDefault(x => x.Id == parent);
            if (next is null) break;
            path.Add(next.Id);
            if (next.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                return new RouteResult("Professional", path, "Nearest ancestor with matching label.");
            current = next;
        }
        return new RouteResult("ControllerFallback", path, "No matching professional route.");
    }
}
