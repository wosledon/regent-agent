using Microsoft.AspNetCore.Authorization;

namespace RegentAgent.Operations;

public static class OperatorAuthorization
{
    public const string ReaderPolicyName = "OperatorReader";
    public const string WriterPolicyName = "OperatorWriter";

    public static IReadOnlyList<string> ReaderRoles { get; } =
        ["SRE", "Ops Viewer", "OperationsAdmin", "TenantAdmin", "SecurityAuditor", "BillingAdmin", "BreakGlassOperator"];

    public static IReadOnlyList<string> WriterRoles { get; } =
        [.. ReaderRoles.Where(role => role != "Ops Viewer")];

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(ReaderPolicyName, policy => policy.RequireAuthenticatedUser().RequireRole(ReaderRoles.ToArray()));
        options.AddPolicy(WriterPolicyName, policy => policy.RequireAuthenticatedUser().RequireRole(WriterRoles.ToArray()));
    }
}
