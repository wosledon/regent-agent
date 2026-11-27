using RegentAgent.Storage;

namespace RegentAgent.Chat;

public sealed record Conversation(string Id, string TenantId, string Kind, string Title, string Status)
{
    public IReadOnlyList<string> MemberEmployeeIds { get; init; } = [];
    public string? OwnerEmployeeId { get; init; }
    public string ScopeType { get; init; } = "Company";
    public string? ScopeId { get; init; }
    public string CreatedBy { get; init; } = "system";
    public string? CreatedByEmployeeId { get; init; }
    public string CreatorType { get; init; } = "System";
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
    public string? SenderEmployeeId { get; init; }
    public string ActorType { get; init; } = "System";
}

public sealed record ChatEmployee(string Id, string DisplayName, string Position);

public sealed class ChatService(JsonDocumentStore<Conversation> conversations, JsonDocumentStore<ChatMessage> messages)
{
    public Task SeedAsync(CancellationToken cancellationToken) => SeedAsync([], cancellationToken);

    public async Task SeedAsync(IReadOnlyCollection<ChatEmployee> employees, CancellationToken cancellationToken)
    {
        var tenantId = "11111111-1111-1111-1111-111111111111";
        var memberIds = employees.Select(x => x.Id).ToArray();
        var controller = employees.FirstOrDefault(x => x.Position == "Controller");
        var engineeringLead = employees.FirstOrDefault(x => x.Position == "Engineering");
        var qualityReviewer = employees.FirstOrDefault(x => x.Position == "Quality");
        var existingConversations = await conversations.GetAllAsync(cancellationToken);
        var existingMessages = await messages.GetAllAsync(cancellationToken);
        var general = existingConversations.FirstOrDefault(x => x.Id == "general" && x.TenantId == tenantId);
        if (general is null)
        {
            await conversations.PutAsync("general", new Conversation("general", tenantId, "Company", "Company HQ", "Active")
            {
                MemberEmployeeIds = memberIds, ScopeType = "Company", CreatedBy = "Controller",
                CreatedByEmployeeId = controller?.Id, CreatorType = "Agent"
            }, cancellationToken);
        }
        else if (general.MemberEmployeeIds.Count == 0 && memberIds.Length > 0)
        {
            await conversations.PutAsync("general", general with { MemberEmployeeIds = memberIds }, cancellationToken);
        }
        if (!existingMessages.Any(x => x.Id == "welcome"))
            await messages.PutAsync("welcome", new ChatMessage("welcome", tenantId, "general", 1, "Controller",
                "Company HQ is online. Use the collaboration workspace to inspect work, groups and conversations.",
                "P2", DateTimeOffset.UtcNow)
            {
                EvidenceRefs = ["workspace:company-hq"], SenderEmployeeId = controller?.Id, ActorType = "Agent"
            }, cancellationToken);

        var deliveryMembers = employees.Where(x => x.Position is "Controller" or "Engineering" or "Quality")
            .Select(x => x.Id).ToArray();
        if (deliveryMembers.Length >= 2)
        {
            if (!existingConversations.Any(x => x.Id == "delivery-room" && x.TenantId == tenantId))
                await conversations.PutAsync("delivery-room", new Conversation("delivery-room", tenantId, "Project", "Delivery Room", "Active")
                {
                    MemberEmployeeIds = deliveryMembers, OwnerEmployeeId = deliveryMembers[0], ScopeType = "Project",
                    ScopeId = "delivery", CreatedBy = "Controller", CreatedByEmployeeId = controller?.Id, CreatorType = "Agent"
                }, cancellationToken);
            if (!existingMessages.Any(x => x.Id == "delivery-kickoff"))
                await messages.PutAsync("delivery-kickoff", new ChatMessage("delivery-kickoff", tenantId, "delivery-room", 1,
                    "Engineering Lead", "Implementation checkpoint is ready for independent quality review.", "P1", DateTimeOffset.UtcNow)
                {
                    MessageType = "ReviewRequest", MentionedEmployeeIds = deliveryMembers, EvidenceRefs = ["task:local-delivery"],
                    SenderEmployeeId = engineeringLead?.Id, ActorType = "Agent"
                }, cancellationToken);
            if (!existingMessages.Any(x => x.Id == "delivery-review"))
                await messages.PutAsync("delivery-review", new ChatMessage("delivery-review", tenantId, "delivery-room", 2,
                    "Quality Reviewer", "Review accepted. Evidence is linked to the delivery timeline.", "P2", DateTimeOffset.UtcNow)
                {
                    MessageType = "StatusUpdate", ReplyToMessageId = "delivery-kickoff", EvidenceRefs = ["review:local-delivery"],
                    SenderEmployeeId = qualityReviewer?.Id, ActorType = "Agent"
                }, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Conversation>> ListAllAsync(string tenantId, CancellationToken cancellationToken) =>
        (await conversations.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<IReadOnlyList<Conversation>> ListAsync(string tenantId, CancellationToken cancellationToken,
        string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) throw new UnauthorizedAccessException();
        return (await conversations.GetAllAsync(cancellationToken)).Where(x => TenantEquals(x.TenantId, tenantId)
            && x.MemberEmployeeIds.Contains(employeeId)).ToArray();
    }

    public async Task<IReadOnlyList<Conversation>> ListObserverConversationsAsync(string tenantId,
        CancellationToken cancellationToken) =>
        (await conversations.GetAllAsync(cancellationToken))
            .Where(x => IsObserverVisible(x, tenantId))
            .OrderBy(x => x.CreatedAt)
            .ToArray();

    public async Task<IReadOnlyList<ChatMessage>> ListObserverMessagesAsync(string conversationId, string tenantId,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (!IsObserverVisible(conversation, tenantId)) throw new UnauthorizedAccessException();
        return (await messages.GetAllAsync(cancellationToken))
            .Where(x => TenantEquals(x.TenantId, tenantId) && x.ConversationId == conversationId)
            .OrderBy(x => x.Seq).ToArray();
    }

    public async Task<bool> CanReadConversationAsync(string conversationId, string tenantId, ChatViewer viewer,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(tenantId, out var requestedTenant) || viewer.TenantId != requestedTenant) return false;
        return viewer switch
        {
            OwnerObserverViewer => await IsObserverVisibleAsync(conversationId, tenantId, cancellationToken),
            EmployeeMemberViewer employee => await IsMemberAsync(conversationId, tenantId, employee.EmployeeId,
                cancellationToken),
            _ => false
        };
    }

    public static bool IsObserverVisible(Conversation conversation, string tenantId)
    {
        if (conversation is null
            || string.IsNullOrWhiteSpace(conversation.Id)
            || conversation.MemberEmployeeIds is null
            || !TenantEquals(conversation.TenantId, tenantId)) return false;
        return IsObserverScope(conversation.Kind) && IsObserverScope(conversation.ScopeType);
    }

    private async Task<bool> IsObserverVisibleAsync(string conversationId, string tenantId,
        CancellationToken cancellationToken)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken);
        return conversation is not null && IsObserverVisible(conversation, tenantId);
    }

    private static bool IsObserverScope(string? value) => value is "Company" or "Project" or "Topic";

    private static bool TenantEquals(string? left, string? right) =>
        Guid.TryParse(left, out var leftTenant) && leftTenant != Guid.Empty
        && Guid.TryParse(right, out var rightTenant) && leftTenant == rightTenant;

    public async Task<bool> IsMemberAsync(string conversationId, string tenantId, string employeeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) return false;
        var conversation = await conversations.GetAsync(conversationId, cancellationToken);
        return conversation is not null && conversation.TenantId == tenantId
            && conversation.MemberEmployeeIds.Contains(employeeId);
    }

    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(string conversationId, string tenantId,
        CancellationToken cancellationToken, string employeeId)
    {
        if (string.IsNullOrWhiteSpace(employeeId)) throw new UnauthorizedAccessException();
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (!TenantEquals(conversation.TenantId, tenantId) || !conversation.MemberEmployeeIds.Contains(employeeId))
            throw new UnauthorizedAccessException();
        return (await messages.GetAllAsync(cancellationToken))
            .Where(x => TenantEquals(x.TenantId, tenantId) && x.ConversationId == conversationId)
            .OrderBy(x => x.Seq).ToArray();
    }

    public async Task<IReadOnlyList<ChatMessage>> ListAllMessagesAsync(string tenantId, CancellationToken cancellationToken) =>
        (await messages.GetAllAsync(cancellationToken)).Where(x => TenantEquals(x.TenantId, tenantId)).OrderBy(x => x.CreatedAt).ToArray();

    public async Task<Conversation> CreateConversationAsync(string tenantId, string title, string kind,
        IReadOnlyCollection<string> memberEmployeeIds, string createdBy, string? createdByEmployeeId,
        string scopeType, string? scopeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120) throw new InvalidOperationException("Conversation title is invalid.");
        if (kind is not ("Direct" or "Project" or "Position" or "Topic" or "Company"))
            throw new InvalidOperationException("Conversation kind is invalid.");
        if (scopeType is not ("Direct" or "Project" or "Position" or "Topic" or "Company"))
            throw new InvalidOperationException("Conversation scope is invalid.");
        var members = memberEmployeeIds.Distinct().ToArray();
        if (members.Length == 0) throw new InvalidOperationException("At least one conversation member is required.");
        var conversation = new Conversation(Guid.NewGuid().ToString("N"), tenantId, kind, title.Trim(), "Active")
        {
            MemberEmployeeIds = members, OwnerEmployeeId = createdByEmployeeId is not null && members.Contains(createdByEmployeeId)
                ? createdByEmployeeId : members[0], ScopeType = scopeType,
            ScopeId = scopeId, CreatedBy = createdBy, CreatedByEmployeeId = createdByEmployeeId, CreatorType = "Agent"
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
        IReadOnlyCollection<string>? evidenceRefs = null, string? idempotencyKey = null,
        string? senderEmployeeId = null, string actorType = "Agent", bool requireMembership = false)
    {
        var conversation = await conversations.GetAsync(conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");
        if (conversation.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (requireMembership && (senderEmployeeId is null || !conversation.MemberEmployeeIds.Contains(senderEmployeeId)))
            throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 8000) throw new InvalidOperationException("Message body is invalid.");
        if (priority is not ("P0" or "P1" or "P2" or "P3")) throw new InvalidOperationException("Message priority is invalid.");
        if (messageType is not ("Chat" or "TaskAssignment" or "DelegationRequest" or "HelpRequest" or "HandoffRequest"
            or "ReviewRequest" or "DecisionRequest" or "Interrupt" or "StatusUpdate" or "Discussion" or "Ack" or "Reject" or "Complete"))
            throw new InvalidOperationException("Message type is invalid.");
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
            EvidenceRefs = evidenceRefs?.Distinct().ToArray() ?? [], IdempotencyKey = idempotencyKey,
            SenderEmployeeId = senderEmployeeId, ActorType = actorType
        };
        await messages.PutAsync(message.Id, message, cancellationToken);
        return message;
    }
}
