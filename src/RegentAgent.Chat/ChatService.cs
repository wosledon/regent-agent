using RegentAgent.Storage;

namespace RegentAgent.Chat;

public sealed record Conversation(string Id, string TenantId, string Kind, string Title, string Status)
{
    public IReadOnlyList<string> MemberEmployeeIds { get; init; } = [];
    public string? OwnerEmployeeId { get; init; }
    public string ScopeType { get; init; } = "Company";
    public string? ScopeId { get; init; }
    public string CreatedBy { get; init; } = "system";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ChatMessage(string Id, string TenantId, string ConversationId, long Seq, string Sender,
    string Body, string Priority, DateTimeOffset CreatedAt)
{
    public string MessageType { get; init; } = "Chat";
    public string? ReplyToMessageId { get; init; }
    public IReadOnlyList<string> MentionedEmployeeIds { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public string? IdempotencyKey { get; init; }
}

public sealed record ChatEmployee(string Id, string DisplayName, string Position);

public sealed class ChatService(JsonDocumentStore<Conversation> conversations, JsonDocumentStore<ChatMessage> messages)
{
    public Task SeedAsync(CancellationToken cancellationToken) => SeedAsync([], cancellationToken);

    public async Task SeedAsync(IReadOnlyCollection<ChatEmployee> employees, CancellationToken cancellationToken)
    {
        if ((await conversations.GetAllAsync(cancellationToken)).Count > 0) return;
        var tenantId = "11111111-1111-1111-1111-111111111111";
        var memberIds = employees.Select(x => x.Id).ToArray();
        await conversations.PutAsync("general", new Conversation("general", tenantId, "Company", "Company HQ", "Active")
        {
            MemberEmployeeIds = memberIds, ScopeType = "Company", CreatedBy = "Controller"
        }, cancellationToken);
        await messages.PutAsync("welcome", new ChatMessage("welcome", tenantId, "general", 1, "Controller",
            "Company HQ is online. Use the collaboration workspace to inspect work, groups and conversations.",
            "P2", DateTimeOffset.UtcNow) { EvidenceRefs = ["workspace:company-hq"] }, cancellationToken);

        var deliveryMembers = employees.Where(x => x.Position is "Controller" or "Engineering" or "Quality")
            .Select(x => x.Id).ToArray();
        if (deliveryMembers.Length >= 2)
        {
            await conversations.PutAsync("delivery-room", new Conversation("delivery-room", tenantId, "Project", "Delivery Room", "Active")
            {
                MemberEmployeeIds = deliveryMembers, OwnerEmployeeId = deliveryMembers[0], ScopeType = "Project",
                ScopeId = "delivery", CreatedBy = "Controller"
            }, cancellationToken);
            await messages.PutAsync("delivery-kickoff", new ChatMessage("delivery-kickoff", tenantId, "delivery-room", 1,
                "Engineering Lead", "Implementation checkpoint is ready for independent quality review.", "P1", DateTimeOffset.UtcNow)
            {
                MessageType = "ReviewRequest", MentionedEmployeeIds = deliveryMembers, EvidenceRefs = ["task:local-delivery"]
            }, cancellationToken);
            await messages.PutAsync("delivery-review", new ChatMessage("delivery-review", tenantId, "delivery-room", 2,
                "Quality Reviewer", "Review accepted. Evidence is linked to the delivery timeline.", "P2", DateTimeOffset.UtcNow)
            {
                MessageType = "StatusUpdate", ReplyToMessageId = "delivery-kickoff", EvidenceRefs = ["review:local-delivery"]
            }, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await conversations.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string conversationId, string tenantId,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.TenantId != tenantId) throw new UnauthorizedAccessException();
        return (await messages.GetAllAsync(cancellationToken))
            .Where(x => x.TenantId == tenantId && x.ConversationId == conversationId)
            .OrderBy(x => x.Seq).ToArray();
    }

    public async Task<IReadOnlyList<ChatMessage>> ListAllMessagesAsync(string tenantId, CancellationToken cancellationToken) =>
        (await messages.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).OrderBy(x => x.CreatedAt).ToArray();

    public async Task<Conversation> CreateConversationAsync(string tenantId, string title, string kind,
        IReadOnlyCollection<string> memberEmployeeIds, string createdBy, string scopeType, string? scopeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120) throw new InvalidOperationException("Conversation title is invalid.");
        if (kind is not ("Direct" or "Project" or "Position" or "Topic" or "Company"))
            throw new InvalidOperationException("Conversation kind is invalid.");
        var members = memberEmployeeIds.Distinct().ToArray();
        if (members.Length == 0) throw new InvalidOperationException("At least one conversation member is required.");
        var conversation = new Conversation(Guid.NewGuid().ToString("N"), tenantId, kind, title.Trim(), "Active")
        {
            MemberEmployeeIds = members, OwnerEmployeeId = members[0], ScopeType = scopeType,
            ScopeId = scopeId, CreatedBy = createdBy
        };
        await conversations.PutAsync(conversation.Id, conversation, cancellationToken);
        return conversation;
    }

    public async Task<Conversation> AddMemberAsync(string conversationId, string tenantId, string employeeId,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (conversation.MemberEmployeeIds.Contains(employeeId)) return conversation;
        var updated = conversation with
        {
            MemberEmployeeIds = conversation.MemberEmployeeIds.Append(employeeId).ToArray()
        };
        await conversations.PutAsync(conversation.Id, updated, cancellationToken);
        return updated;
    }

    public async Task<ChatMessage> SendAsync(string conversationId, string tenantId, string sender, string body,
        string priority, CancellationToken cancellationToken, string? replyToMessageId = null,
        IReadOnlyCollection<string>? mentionedEmployeeIds = null, string messageType = "Chat",
        IReadOnlyCollection<string>? evidenceRefs = null, string? idempotencyKey = null)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 8000) throw new InvalidOperationException("Message body is invalid.");
        if (priority is not ("P0" or "P1" or "P2" or "P3")) throw new InvalidOperationException("Message priority is invalid.");
        var existing = await messages.GetAllAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var repeated = existing.FirstOrDefault(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey);
            if (repeated is not null) return repeated;
        }
        if (replyToMessageId is not null && !existing.Any(x => x.TenantId == tenantId && x.ConversationId == conversationId && x.Id == replyToMessageId))
            throw new InvalidOperationException("Reply target does not belong to this conversation.");
        var seq = existing.Where(x => x.TenantId == tenantId && x.ConversationId == conversationId)
            .Select(x => x.Seq).DefaultIfEmpty().Max() + 1;
        var message = new ChatMessage(Guid.NewGuid().ToString("N"), tenantId, conversationId, seq, sender, body.Trim(),
            priority, DateTimeOffset.UtcNow)
        {
            MessageType = messageType, ReplyToMessageId = replyToMessageId,
            MentionedEmployeeIds = mentionedEmployeeIds?.Distinct().ToArray() ?? [],
            EvidenceRefs = evidenceRefs?.Distinct().ToArray() ?? [], IdempotencyKey = idempotencyKey
        };
        await messages.PutAsync(message.Id, message, cancellationToken);
        return message;
    }
}
