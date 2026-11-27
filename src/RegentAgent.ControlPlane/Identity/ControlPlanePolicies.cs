namespace RegentAgent.ControlPlane.Identity;

public static class ControlPlanePolicies
{
    public const string TenantMember = "TenantMember";
    public const string TenantWriter = "TenantWriter";
    public const string ApprovalDecider = "ApprovalDecider";
    public const string ChatRead = "ChatRead";
    public const string CollaborationRead = "CollaborationRead";

    public const string TenantWriteCapability = "tenant.write";
    public const string ApproveEmployeeOffboardCapability = "approval.employee.offboard";
    public const string ApproveEmployeeTerminateCapability = "approval.employee.terminate";
    public const string ApproveHiringHireCapability = "approval.hiring.hire";
    public const string ApproveHiringRehireCapability = "approval.hiring.rehire";
    public const string ChatReadCapability = "chat.read";
    public const string CollaborationReadCapability = "collaboration.read";
    public const string RestrictedToolActivationCapability = "tools.restricted.activate";
}
