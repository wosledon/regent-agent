using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class ApprovalAuthority(RegentDbContext db)
{
    public async Task EnsureCanDecideAsync(Guid tenantId, Guid actorId, string actionType,
        CancellationToken cancellationToken)
    {
        var requiredCapability = RequiredCapability(actionType);
        if (requiredCapability is not null)
        {
            await EnsureCapabilityAsync(tenantId, actorId, requiredCapability, cancellationToken);
            return;
        }

        await EnsureGenericDecisionAuthorityAsync(tenantId, actorId, cancellationToken);
    }

    public async Task EnsureCanExecuteAsync(Guid tenantId, Guid actorId, string actionType,
        CancellationToken cancellationToken)
    {
        var requiredCapability = RequiredCapability(actionType)
            ?? throw new InvalidOperationException("Approval action does not have an execution authority policy.");
        await EnsureCapabilityAsync(tenantId, actorId, requiredCapability, cancellationToken);
    }

    private async Task EnsureCapabilityAsync(Guid tenantId, Guid actorId, string requiredCapability,
        CancellationToken cancellationToken)
    {
        var (role, capabilities) = await LoadAuthorityAsync(tenantId, actorId, cancellationToken);
        if (role is "Owner" or "CoOwner") return;
        if (!capabilities.Contains(requiredCapability, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Approval authority capability is required.");
    }

    private async Task EnsureGenericDecisionAuthorityAsync(Guid tenantId, Guid actorId,
        CancellationToken cancellationToken)
    {
        var (role, capabilities) = await LoadAuthorityAsync(tenantId, actorId, cancellationToken);
        if (role is "Owner" or "CoOwner") return;
        if (capabilities.Contains(ControlPlanePolicies.TenantWriteCapability, StringComparer.OrdinalIgnoreCase)
            || capabilities.Any(value => value.StartsWith("approval.", StringComparison.OrdinalIgnoreCase)))
            return;
        throw new UnauthorizedAccessException("Approval decision authority is required.");
    }

    private async Task<(string Role, string[] Capabilities)> LoadAuthorityAsync(Guid tenantId, Guid actorId,
        CancellationToken cancellationToken)
    {
        var membership = await db.Memberships.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == actorId, cancellationToken);
        if (membership is null)
            throw new UnauthorizedAccessException("Current tenant membership is required for approval authority.");
        string[] capabilities;
        try { capabilities = System.Text.Json.JsonSerializer.Deserialize<string[]>(membership.CapabilitiesJson) ?? []; }
        catch (System.Text.Json.JsonException)
        {
            throw new UnauthorizedAccessException("Approval authority capabilities are invalid.");
        }
        return (membership.Role, capabilities);
    }

    public static string? RequiredCapability(string actionType) => actionType.Trim().ToLowerInvariant() switch
    {
        ApprovalActionTypes.EmployeeOffboard => ControlPlanePolicies.ApproveEmployeeOffboardCapability,
        ApprovalActionTypes.EmployeeTerminate => ControlPlanePolicies.ApproveEmployeeTerminateCapability,
        ApprovalActionTypes.HiringHire => ControlPlanePolicies.ApproveHiringHireCapability,
        ApprovalActionTypes.HiringRehire => ControlPlanePolicies.ApproveHiringRehireCapability,
        _ => null
    };
}
