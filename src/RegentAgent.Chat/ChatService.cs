using RegentAgent.Storage;

namespace RegentAgent.Chat;

public sealed record Conversation(string Id, string TenantId, string Kind, string Title, string Status);
public sealed record ChatMessage(string Id, string TenantId, string ConversationId, long Seq, string Sender, string Body, string Priority, DateTimeOffset CreatedAt);

public sealed class ChatService(JsonDocumentStore<Conversation> conversations, JsonDocumentStore<ChatMessage> messages)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await conversations.GetAllAsync(cancellationToken)).Count > 0) return;
        await conversations.PutAsync("general", new Conversation("general", "11111111-1111-1111-1111-111111111111", "Company", "General", "Active"), cancellationToken);
        await messages.PutAsync("welcome", new ChatMessage("welcome", "11111111-1111-1111-1111-111111111111", "general", 1, "Controller",
            "Local workspace is ready. Create a task or review the dashboard.", "P2", DateTimeOffset.UtcNow), cancellationToken);
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await conversations.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string conversationId, CancellationToken cancellationToken) =>
        (await messages.GetAllAsync(cancellationToken)).Where(x => x.ConversationId == conversationId).OrderBy(x => x.Seq).ToArray();

    public async Task<ChatMessage> SendAsync(string conversationId, string tenantId, string sender, string body, string priority, CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken) ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.TenantId != tenantId) throw new UnauthorizedAccessException();
        var existing = await messages.GetAllAsync(cancellationToken);
        var seq = existing.Where(x => x.ConversationId == conversationId).Select(x => x.Seq).DefaultIfEmpty().Max() + 1;
        var message = new ChatMessage(Guid.NewGuid().ToString("N"), tenantId, conversationId, seq, sender, body, priority, DateTimeOffset.UtcNow);
        await messages.PutAsync(message.Id, message, cancellationToken);
        return message;
    }
}
