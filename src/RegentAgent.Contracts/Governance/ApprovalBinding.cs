using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RegentAgent.Contracts.Governance;

public static class ApprovalActionTypes
{
    public const string EmployeeOffboard = "employee.offboard";
    public const string EmployeeTerminate = "employee.terminate";
    public const string HiringHire = "hiring.hire";
    public const string HiringRehire = "hiring.rehire";
    public const string Offboard = EmployeeOffboard;
    public const string Terminate = EmployeeTerminate;
    public const string Hire = HiringHire;
    public const string Rehire = HiringRehire;
}

public sealed record ApprovalBinding(
    Guid TenantId,
    string ActionType,
    Guid? TargetEmployeeId = null,
    Guid? TargetCandidateId = null,
    Guid? TargetPositionId = null)
{
    public string NormalizedActionType => (ActionType ?? string.Empty).Trim().ToLowerInvariant();

    public string ComputeHash() => ComputeV2Hash();

    public string ComputeV2Hash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            tenant_id = TenantId.ToString("D"),
            action_type = NormalizedActionType,
            target_employee_id = TargetEmployeeId?.ToString("D"),
            target_candidate_id = TargetCandidateId?.ToString("D"),
            target_position_id = TargetPositionId?.ToString("D")
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public string ComputeLegacyV1Hash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            action_type = NormalizedActionType,
            target_employee_id = TargetEmployeeId?.ToString("D")
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public bool MatchesV2Hash(string? hash) =>
        !string.IsNullOrWhiteSpace(hash)
        && string.Equals(hash, ComputeV2Hash(), StringComparison.Ordinal);

    public bool MatchesLegacyV1Hash(string? hash) =>
        !string.IsNullOrWhiteSpace(hash)
        && string.Equals(hash, ComputeLegacyV1Hash(), StringComparison.Ordinal);
}
