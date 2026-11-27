using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.Contracts.Identity;
using RegentAgent.Operations;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class AuthBootstrapTests
{
    private static readonly string[] ExpectedReaderRoles =
    [
        "SRE", "Ops Viewer", "OperationsAdmin", "TenantAdmin", "SecurityAuditor", "BillingAdmin", "BreakGlassOperator"
    ];

    public static TheoryData<string> OperatorRoles { get; } = new(ExpectedReaderRoles);

    [Fact]
    public void Operator_reader_policy_contains_exact_roles()
    {
        var options = new AuthorizationOptions();
        OperatorAuthorization.Configure(options);

        var allowedRoles = options.GetPolicy(OperatorAuthorization.ReaderPolicyName)!
            .Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles;

        Assert.Equal(ExpectedReaderRoles.OrderBy(role => role, StringComparer.Ordinal),
            allowedRoles.OrderBy(role => role, StringComparer.Ordinal));
    }

    [Fact]
    public void Operator_writer_policy_contains_reader_roles_except_ops_viewer()
    {
        var options = new AuthorizationOptions();
        OperatorAuthorization.Configure(options);

        var allowedRoles = options.GetPolicy(OperatorAuthorization.WriterPolicyName)!
            .Requirements.OfType<RolesAuthorizationRequirement>().Single().AllowedRoles;

        Assert.Equal(ExpectedReaderRoles.Where(role => role != "Ops Viewer").OrderBy(role => role, StringComparer.Ordinal),
            allowedRoles.OrderBy(role => role, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OperatorRoles))]
    public async Task Operator_reader_accepts_every_role_and_writer_excludes_only_ops_viewer(string role)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "subject"), new Claim(ClaimTypes.Role, role)], "test"));

        var (reader, writer) = await AuthorizeAsync(user);

        Assert.True(reader.Succeeded);
        Assert.Equal(role != "Ops Viewer", writer.Succeeded);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("")]
    public async Task Authenticated_non_operators_fail_both_operator_policies(string role)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "subject") };
        if (role.Length > 0) claims.Add(new Claim(ClaimTypes.Role, role));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var (reader, writer) = await AuthorizeAsync(user);

        Assert.False(reader.Succeeded);
        Assert.False(writer.Succeeded);
    }

    [Fact]
    public async Task Anonymous_principals_fail_both_operator_policies()
    {
        var (reader, writer) = await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(reader.Succeeded);
        Assert.False(writer.Succeeded);
    }

    private static async Task<(AuthorizationResult Reader, AuthorizationResult Writer)> AuthorizeAsync(ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(OperatorAuthorization.Configure);
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        return (
            await authorization.AuthorizeAsync(user, null, OperatorAuthorization.ReaderPolicyName),
            await authorization.AuthorizeAsync(user, null, OperatorAuthorization.WriterPolicyName));
    }

    [Fact]
    public void Operator_bootstrap_summary_has_no_tenant_or_session_identifier()
    {
        var summary = new OperatorSessionSummaryDto("operator@example.test", "Demo Operator", "SRE", "Cookie", "Operator", ["ops.read"]);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(new[] { "email", "display_name", "role", "auth_method", "session_kind", "capabilities" },
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }
}
