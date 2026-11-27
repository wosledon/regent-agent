namespace RegentAgent.Chat;

public abstract record ChatViewer(Guid TenantId);

public sealed record EmployeeMemberViewer(Guid TenantId, string EmployeeId) : ChatViewer(TenantId);

public sealed record OwnerObserverViewer(Guid TenantId) : ChatViewer(TenantId);
