using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Tests;

internal static class TestAuthenticationHeaders
{
    public const string Authenticated = "X-Test-Authenticated";
    public const string TenantId = "X-Test-Tenant-Id";
    public const string UserId = "X-Test-User-Id";
    public const string Name = "X-Test-Name";
    public const string Role = "X-Test-Role";
    public const string AuthMethod = "X-Test-Auth-Method";
    public const string EmployeeId = "X-Test-Employee-Id";
    public const string SessionId = "X-Test-Session-Id";
    public const string Capabilities = "X-Test-Capabilities";
    public const string ManageMembership = "X-Test-Manage-Membership";
}

internal sealed class TestAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "Test";
}

internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<TestAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    RegentDbContext db,
    TestMembershipRegistry memberships) : AuthenticationHandler<TestAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!string.Equals(Context.Request.Headers[TestAuthenticationHeaders.Authenticated], "true",
                StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var claims = new List<Claim>();
        AddClaim(claims, "tenant_id", TestAuthenticationHeaders.TenantId);
        AddClaim(claims, ClaimTypes.NameIdentifier, TestAuthenticationHeaders.UserId);
        AddClaim(claims, ClaimTypes.Name, TestAuthenticationHeaders.Name);
        AddClaim(claims, ClaimTypes.Role, TestAuthenticationHeaders.Role);
        AddClaim(claims, "auth_method", TestAuthenticationHeaders.AuthMethod);
        AddClaim(claims, "employee_id", TestAuthenticationHeaders.EmployeeId);
        AddClaim(claims, "session_id", TestAuthenticationHeaders.SessionId);
        var capabilities = Context.Request.Headers[TestAuthenticationHeaders.Capabilities]
            .ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        claims.AddRange(capabilities.Select(value => new Claim("capability", value)));
        await EnsureMembershipAsync(claims, capabilities);
        var identity = new ClaimsIdentity(claims, TestAuthenticationOptions.SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),
            TestAuthenticationOptions.SchemeName));
    }

    private async Task EnsureMembershipAsync(IReadOnlyList<Claim> claims, string[] capabilities)
    {
        if (!bool.TryParse(Context.Request.Headers[TestAuthenticationHeaders.ManageMembership], out var manage)
            || !manage
            || !Guid.TryParse(claims.FirstOrDefault(value => value.Type == "tenant_id")?.Value, out var tenantId)
            || tenantId == Guid.Empty
            || !Guid.TryParse(claims.FirstOrDefault(value => value.Type == ClaimTypes.NameIdentifier)?.Value, out var userId)
            || userId == Guid.Empty
            || !memberships.TryMark(tenantId, userId))
            return;

        if (await db.Memberships.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId,
                Context.RequestAborted))
            return;

        var role = claims.FirstOrDefault(value => value.Type == ClaimTypes.Role)?.Value;
        if (string.IsNullOrWhiteSpace(role)) role = "Member";
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, UserId = userId, Role = role,
            CapabilitiesJson = System.Text.Json.JsonSerializer.Serialize(capabilities)
        });
        await db.SaveChangesAsync(Context.RequestAborted);
    }

    private void AddClaim(ICollection<Claim> claims, string type, string header)
    {
        var value = Context.Request.Headers[header].ToString();
        if (!string.IsNullOrWhiteSpace(value)) claims.Add(new Claim(type, value));
    }
}

internal static class TestIdentityRequest
{
    public static void Apply(HttpRequestMessage request, Guid tenantId, string role = "Member",
        string? authMethod = "Oidc", IReadOnlyList<string>? capabilities = null, string? employeeId = null,
        string name = "Test User", Guid? userId = null, Guid? sessionId = null)
    {
        request.Headers.Add(TestAuthenticationHeaders.Authenticated, "true");
        request.Headers.Add(TestAuthenticationHeaders.ManageMembership, "true");
        request.Headers.Add(TestAuthenticationHeaders.TenantId, tenantId.ToString());
        request.Headers.Add(TestAuthenticationHeaders.UserId, (userId ?? Guid.NewGuid()).ToString());
        request.Headers.Add(TestAuthenticationHeaders.Name, name);
        request.Headers.Add(TestAuthenticationHeaders.Role, role);
        if (authMethod is not null) request.Headers.Add(TestAuthenticationHeaders.AuthMethod, authMethod);
        if (employeeId is not null) request.Headers.Add(TestAuthenticationHeaders.EmployeeId, employeeId);
        if (sessionId is not null) request.Headers.Add(TestAuthenticationHeaders.SessionId, sessionId.Value.ToString());
        if (capabilities is { Count: > 0 })
            request.Headers.Add(TestAuthenticationHeaders.Capabilities, string.Join(',', capabilities));
    }
}

internal static class TestAuthenticationExtensions
{
    public static IServiceCollection AddTestAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<TestMembershipRegistry>();
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationOptions.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationOptions.SchemeName;
            })
            .AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(TestAuthenticationOptions.SchemeName, _ => { });
        return services;
    }
}
