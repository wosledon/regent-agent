using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Enums;

namespace RegentAgent.ControlPlane.Identity;

public sealed record CurrentMembership(Guid TenantId, Guid UserId, string Role, IReadOnlySet<string> Capabilities);

public sealed record ResolvedChatViewer(ChatViewer Viewer, CurrentMembership Membership);

public sealed class MembershipAuthority(RegentDbContext db)
{
    public async Task<CurrentMembership?> FindAsync(ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true
            || !TryGetSingleGuidClaim(user, "tenant_id", out var tenantId)
            || !TryGetSingleGuidClaim(user, ClaimTypes.NameIdentifier, out var userId))
            return null;

        return await FindAsync(tenantId, userId, cancellationToken);
    }

    public async Task<ResolvedChatViewer?> ResolveChatViewerAsync(ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true
            || !TryGetSingleGuidClaim(user, "tenant_id", out var tenantId)
            || !TryGetSingleGuidClaim(user, ClaimTypes.NameIdentifier, out var userId))
            return null;

        var employeeClaims = user.FindAll("employee_id").ToArray();
        if (employeeClaims.Length > 1) return null;
        var employeeId = employeeClaims.Length == 1 ? employeeClaims[0].Value : null;
        return await ResolveChatViewerAsync(tenantId, userId, employeeId, cancellationToken);
    }

    public async Task<ResolvedChatViewer?> ResolveChatViewerAsync(Guid tenantId, Guid userId,
        string? employeeId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty) return null;
        var membership = await FindAsync(tenantId, userId, cancellationToken);
        if (membership is null) return null;

        if (employeeId is null)
            return membership.Role is "Owner" or "CoOwner"
                ? new ResolvedChatViewer(new OwnerObserverViewer(tenantId), membership)
                : null;

        if (!Guid.TryParse(employeeId, out var employeeKey) || employeeKey == Guid.Empty) return null;
        var validEmployee = await db.Employees.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.Id == employeeKey && x.State != EmployeeState.Terminated,
            cancellationToken)
            && await db.EmploymentPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.EmployeeId == employeeKey && x.EndedAt == null,
            cancellationToken);
        return validEmployee
            ? new ResolvedChatViewer(new EmployeeMemberViewer(tenantId, employeeKey.ToString()), membership)
            : null;
    }

    public async Task<CurrentMembership?> FindAsync(Guid tenantId, Guid userId,
        CancellationToken cancellationToken = default)
    {
        var membership = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        if (membership is null || string.IsNullOrWhiteSpace(membership.Role)
            || !TryParseCapabilities(membership.CapabilitiesJson, out var capabilities)) return null;

        return new CurrentMembership(tenantId, userId, membership.Role, capabilities);
    }

    private static bool TryParseCapabilities(string json, out IReadOnlySet<string> capabilities)
    {
        capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return false;
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) return false;
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value)) return false;
                values.Add(value);
            }
            capabilities = values;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string ComputeAuthorityHash(string role, IEnumerable<string> capabilities)
    {
        var normalizedCapabilities = capabilities
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        var authority = $"{role.Trim().ToLowerInvariant()}\n{string.Join('\n', normalizedCapabilities)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authority)));
    }

    private static bool TryGetSingleGuidClaim(ClaimsPrincipal user, string claimType, out Guid value)
    {
        value = Guid.Empty;
        var claims = user.FindAll(claimType).ToArray();
        return claims.Length == 1
            && Guid.TryParse(claims[0].Value, out value)
            && value != Guid.Empty;
    }
}
