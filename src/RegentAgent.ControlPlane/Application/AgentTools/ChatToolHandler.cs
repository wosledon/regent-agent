using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class ChatToolHandler(ChatService chat, RegentDbContext db) : IAgentToolHandler
{
    public string Version => "1.0.0";
    public IReadOnlyList<string> Capabilities => ["chat:write"];
    public string ToolName => "chat";

    public async Task<object> ExecuteAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var operation = AgentToolArguments.RequiredString(arguments, "operation");
        return operation switch
        {
            "send" => await SendAsync(context, arguments, cancellationToken),
            "create_group" => await CreateGroupAsync(context, arguments, cancellationToken),
            "add_member" => await AddMemberAsync(context, arguments, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported chat tool operation.")
        };
    }

    private async Task<object> SendAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var conversationId = AgentToolArguments.RequiredString(arguments, "conversationId");
        var mentions = AgentToolArguments.OptionalStrings(arguments, "mentionedEmployeeIds");
        await RequireTenantMembersAsync(context.Identity.TenantId, mentions, cancellationToken);
        return await chat.SendAsync(conversationId, context.Identity.TenantId.ToString(), context.ActorName,
            AgentToolArguments.RequiredString(arguments, "body"),
            AgentToolArguments.OptionalString(arguments, "priority", "P2"), cancellationToken,
            AgentToolArguments.OptionalString(arguments, "replyToMessageId"),
            mentions, AgentToolArguments.OptionalString(arguments, "messageType", "Chat"),
            AgentToolArguments.OptionalStrings(arguments, "evidenceRefs"),
            AgentToolArguments.OptionalString(arguments, "idempotencyKey"),
            context.Identity.EmployeeId.ToString(), "Agent", true);
    }

    private async Task<object> CreateGroupAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var members = AgentToolArguments.RequiredStrings(arguments, "memberEmployeeIds").Distinct().ToArray();
        if (!members.Contains(context.Identity.EmployeeId.ToString(), StringComparer.Ordinal))
            throw new InvalidOperationException("The creating Agent must be a conversation member.");
        await RequireTenantMembersAsync(context.Identity.TenantId, members, cancellationToken);
        return await chat.CreateConversationAsync(context.Identity.TenantId.ToString(),
            AgentToolArguments.RequiredString(arguments, "title"),
            AgentToolArguments.RequiredString(arguments, "kind"), members, context.ActorName,
            context.Identity.EmployeeId.ToString(), AgentToolArguments.RequiredString(arguments, "scopeType"),
            AgentToolArguments.OptionalString(arguments, "scopeId"), cancellationToken);
    }

    private async Task<object> AddMemberAsync(AgentToolContext context, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var conversationId = AgentToolArguments.RequiredString(arguments, "conversationId");
        var employeeId = AgentToolArguments.RequiredString(arguments, "employeeId");
        await RequireTenantMembersAsync(context.Identity.TenantId, [employeeId], cancellationToken);
        var visible = await chat.ListAsync(context.Identity.TenantId.ToString(), cancellationToken,
            context.Identity.EmployeeId.ToString());
        if (!visible.Any(x => x.Id == conversationId)) throw new UnauthorizedAccessException();
        return await chat.AddMemberAsync(conversationId, context.Identity.TenantId.ToString(), employeeId,
            cancellationToken);
    }

    private async Task RequireTenantMembersAsync(Guid tenantId, IReadOnlyList<string> employeeIds,
        CancellationToken cancellationToken)
    {
        var ids = employeeIds.ToHashSet(StringComparer.Ordinal);
        var existing = (await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => x.Id).ToListAsync(cancellationToken)).Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
        if (ids.Any(id => !Guid.TryParse(id, out _)) || ids.Any(id => !existing.Contains(id)))
            throw new InvalidOperationException("Chat members must belong to the Agent tenant.");
    }
}
