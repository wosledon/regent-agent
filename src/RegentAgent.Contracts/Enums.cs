namespace RegentAgent.Contracts.Enums;

public enum ProjectStage
{
    BriefReady,
    Approved,
    Building,
    Verifying,
    Releasing,
    Live,
    Iterating,
    Terminated,
    Proposed,
    Active,
    Paused,
    Completed,
    Cancelled
}

public enum ProjectHealth
{
    OnTrack,
    AtRisk,
    Blocked,
    PendingAcceptance,
    Live,
    Failed
}

public enum Severity
{
    Low,
    Medium,
    High,
    Critical
}

public enum Freshness
{
    Live,
    Fresh,
    Stale,
    Unknown
}

public enum ApprovalState
{
    Pending,
    Approved,
    Rejected,
    Expired
}

public enum EmployeeState
{
    Active,
    Busy,
    Sleeping,
    RateLimited,
    Degraded,
    Unavailable,
    PendingReview,
    Frozen,
    Terminated
}
