using System.Text.Json;
using Microsoft.AspNetCore.Http;
using RegentAgent.ControlPlane.Application;

namespace RegentAgent.ControlPlane.Identity;

public static class TenantSessionAccessPolicy
{
    private const string StandardSessionKind = "Standard";
    private const string AccountAccessAction = "account.access";
    private const string SsoRestoreAction = "sso.restore";

    public static bool IsRestricted(AuthSessionRecord session)
    {
        if (!string.Equals(session.SessionKind, StandardSessionKind, StringComparison.OrdinalIgnoreCase))
            return true;
        return !TryGetAllowedActions(session, out var actions) || actions.Count > 0;
    }

    public static bool CanUseRealtime(AuthSessionRecord session) => !IsRestricted(session);

    public static bool RequestAllowed(AuthSessionRecord session, PathString path, string method, Guid tenantId)
    {
        if (!IsRestricted(session)) return true;
        if (HttpMethods.IsOptions(method) || HttpMethods.IsHead(method)) return false;
        var action = ResolveAction(path, tenantId);
        return action is not null
            && TryGetAllowedActions(session, out var actions)
            && actions.Contains(action);
    }

    private static string? ResolveAction(PathString path, Guid tenantId)
    {
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length == 2 && segments[0] == "auth" && segments[1] is "logout" or "me")
            return AccountAccessAction;
        if (segments.Length == 2 && segments[0] == "auth" && segments[1] == "sessions")
            return AccountAccessAction;
        if (segments.Length == 4 && segments[0] == "auth" && segments[1] == "sessions"
            && Guid.TryParse(segments[2], out _) && segments[3] == "revoke")
            return AccountAccessAction;
        if (segments.Length >= 4 && segments[0] == "v1" && segments[1] == "tenants"
            && Guid.TryParse(segments[2], out var requestedTenant) && requestedTenant == tenantId)
        {
            if (segments[3] == "authentication-policy") return SsoRestoreAction;
            if (segments[3] is "identity-providers" or "oidc") return SsoRestoreAction;
        }
        return null;
    }

    private static bool TryGetAllowedActions(AuthSessionRecord session, out IReadOnlySet<string> actions)
    {
        actions = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(session.AllowedActionsJson)) return false;
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(session.AllowedActionsJson) ?? [];
            if (values.Any(string.IsNullOrWhiteSpace)) return false;
            actions = values.ToHashSet(StringComparer.Ordinal);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
