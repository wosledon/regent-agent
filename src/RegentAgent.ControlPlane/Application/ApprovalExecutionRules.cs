using System.Linq.Expressions;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

internal static class ApprovalExecutionRules
{
    private static readonly Expression<Func<ApprovalRecord, bool>> ValidDeciderExpression =
        approval => approval.DecidedBy != null
            && approval.DecidedBy != Guid.Empty
            && approval.DecidedBy != approval.RequesterUserId;

    private static readonly Func<ApprovalRecord, bool> ValidDecider = ValidDeciderExpression.Compile();

    public static Expression<Func<ApprovalRecord, bool>> HasValidDeciderExpression => ValidDeciderExpression;

    public static bool HasValidDecider(ApprovalRecord approval) => ValidDecider(approval);
}
