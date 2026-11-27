using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.ControlPlane.Application;

internal static class ApprovalExecutionEvent
{
    public const string EventType = "approval.execution.committed";

    public static Guid DeterministicId(Guid approvalId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{EventType}:{approvalId:D}"));
        return new Guid(bytes[..16]);
    }
}
