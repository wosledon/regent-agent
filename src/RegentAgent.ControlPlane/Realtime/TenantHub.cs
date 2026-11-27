using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using Microsoft.EntityFrameworkCore;

namespace RegentAgent.ControlPlane.Realtime;

public sealed class TenantHub(ChatService chat, ITenantSessionValidator sessions,
    MembershipAuthority membership, TenantHubConnectionRegistry connections, RegentDbContext? db = null) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var tenantId = RequiredTenantId();
        var userId = RequiredGuid(ClaimTypes.NameIdentifier, "User identity is required.");
        var sessionId = RequiredGuid("session_id", "Session identity is required.");
        var employeeId = Context.User?.FindFirstValue("employee_id");
        var credentialEpoch = ReadCredentialEpoch();
        if (employeeId is not null && credentialEpoch is null && db is not null)
        {
            if (!Guid.TryParse(employeeId, out var employeeKey) || employeeKey == Guid.Empty)
                throw new HubException("Employee identity is invalid.");
            credentialEpoch = await db.EmploymentPeriods.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeKey && x.EndedAt == null)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(Context.ConnectionAborted);
            if (credentialEpoch is null) throw new HubException("Active employment is required.");
        }
        if (!await sessions.ValidateAsync(sessionId, userId, tenantId, employeeId, credentialEpoch,
                Context.ConnectionAborted))
            throw new HubException("Session access denied.");
        await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId.ToString()));
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId.ToString(), userId.ToString()));
        connections.Connected(Context.ConnectionId,
            new TenantHubConnection(sessionId, tenantId, userId, employeeId, Context.Abort, credentialEpoch));
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinConversation(string conversationId)
    {
        await RequireActiveSessionAsync();
        var tenantId = RequiredTenantId();
        var viewer = await membership.ResolveChatViewerAsync(Context.User!, Context.ConnectionAborted);
        if (!ObserverReadPolicy.CanRead(viewer, ControlPlanePolicies.ChatReadCapability))
            throw new HubException("Chat read permission is required.");
        if (string.IsNullOrWhiteSpace(conversationId)
            || !await chat.CanReadConversationAsync(conversationId, tenantId.ToString(), viewer!.Viewer,
                Context.ConnectionAborted))
            throw new HubException("Conversation access denied.");
        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(tenantId.ToString(), conversationId));
    }

    public async Task LeaveConversation(string conversationId)
    {
        await RequireActiveSessionAsync();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ConversationGroup(RequiredTenantId().ToString(), conversationId));
    }

    public static string TenantGroup(string tenantId) => tenantId;

    public static string UserGroup(string tenantId, string userId) => $"user:{tenantId}:{userId}";

    public static string ConversationGroup(string tenantId, string conversationId) =>
        $"conversation:{tenantId}:{conversationId}";

    private async Task RequireActiveSessionAsync()
    {
        var tenantId = RequiredTenantId();
        var userId = RequiredGuid(ClaimTypes.NameIdentifier, "User identity is required.");
        var sessionId = RequiredGuid("session_id", "Session identity is required.");
        var employeeId = Context.User?.FindFirstValue("employee_id");
        if (!connections.TryGet(Context.ConnectionId, out var connection) || connection is null
            || !await sessions.ValidateAsync(sessionId, userId, tenantId, employeeId,
                connection.CredentialEpoch, Context.ConnectionAborted))
        {
            connections.Disconnected(Context.ConnectionId);
            Context.Abort();
            throw new HubException("Session access denied.");
        }
    }

    private Guid? ReadCredentialEpoch()
    {
        var value = Context.User?.FindFirstValue("credential_epoch");
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Guid.TryParse(value, out var epoch) || epoch == Guid.Empty)
            throw new HubException("Credential epoch is invalid.");
        return epoch;
    }

    private Guid RequiredTenantId() => RequiredGuid("tenant_id", "Tenant identity is required.");

    private Guid RequiredGuid(string claimType, string message) =>
        Guid.TryParse(Context.User?.FindFirstValue(claimType), out var value) && value != Guid.Empty
            ? value : throw new HubException(message);
}
