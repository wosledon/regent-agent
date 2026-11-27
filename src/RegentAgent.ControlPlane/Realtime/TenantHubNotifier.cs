using Microsoft.AspNetCore.SignalR;
using RegentAgent.Chat;

namespace RegentAgent.ControlPlane.Realtime;

public sealed record TenantInvalidation(string ResourceType);

public sealed class TenantHubNotifier(IHubContext<TenantHub> hub)
{
    public Task MessageCreatedAsync(string tenantId, ChatMessage message, CancellationToken cancellationToken)
    {
        var invalidation = new TenantInvalidation("message");
        return Task.WhenAll(
            hub.Clients.Group(TenantHub.ConversationGroup(tenantId, message.ConversationId))
                .SendAsync("message.created", message, cancellationToken),
            hub.Clients.Group(TenantHub.TenantGroup(tenantId))
                .SendAsync("message.created", invalidation, cancellationToken));
    }

    public Task ApprovalChangedAsync(string tenantId, Guid approvalId, CancellationToken cancellationToken) =>
        hub.Clients.Group(TenantHub.TenantGroup(tenantId))
            .SendAsync("approval.changed", new TenantInvalidation("approval"), cancellationToken);

    public Task CoordinationChangedAsync(string tenantId, Guid coordinationId, CancellationToken cancellationToken) =>
        hub.Clients.Group(TenantHub.TenantGroup(tenantId))
            .SendAsync("coordination.changed", new TenantInvalidation("coordination"), cancellationToken);
}
