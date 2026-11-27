using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class ChatObserverAccessTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OwnerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CoOwnerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("Owner", "22222222-2222-2222-2222-222222222222")]
    [InlineData("CoOwner", "33333333-3333-3333-3333-333333333333")]
    public async Task Owner_without_employee_or_capabilities_reads_shared_graph_trace_list_and_messages(
        string role, string userIdValue)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var userId = Guid.Parse(userIdValue);
        await SetMembershipAsync(factory, userId, role, "[]");
        using var scope = factory.Services.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        var member = await FirstEmployeeIdAsync(factory);
        var conversation = await chat.CreateConversationAsync(TenantId.ToString(), $"Shared {role} room", "Project",
            [member], "Agent", member, "Project", null, CancellationToken.None);
        var message = await chat.SendAsync(conversation.Id, TenantId.ToString(), "Agent", $"Shared {role} body",
            "P2", CancellationToken.None, senderEmployeeId: member, actorType: "Agent", requireMembership: true);

        using var list = Request("/v1/conversations", userId);
        using var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(conversation.Id, await listResponse.Content.ReadAsStringAsync());

        using var messages = Request($"/v1/conversations/{conversation.Id}/messages", userId);
        using var messagesResponse = await client.SendAsync(messages);
        Assert.Equal(HttpStatusCode.OK, messagesResponse.StatusCode);
        Assert.Contains(message.Body, await messagesResponse.Content.ReadAsStringAsync());

        using var graph = Request("/v1/collaboration/graph", userId);
        using var graphResponse = await client.SendAsync(graph);
        Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
        var graphJson = await graphResponse.Content.ReadAsStringAsync();
        Assert.Contains(conversation.Title, graphJson, StringComparison.Ordinal);
        Assert.Contains(message.Body, graphJson, StringComparison.Ordinal);

        using var trace = Request($"/v1/collaboration/trace?entityType=conversation&entityId={conversation.Id}", userId);
        using var traceResponse = await client.SendAsync(trace);
        Assert.Equal(HttpStatusCode.OK, traceResponse.StatusCode);
        Assert.Contains(message.Body, await traceResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Observer_excludes_private_malformed_and_mismatched_conversations_from_every_read_surface()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        await SetMembershipAsync(factory, OwnerId, "Owner", "[]");
        using var scope = factory.Services.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        _ = await scope.ServiceProvider.GetRequiredService<CollaborationVisualizationService>()
            .GetGraphAsync(TenantId, new OwnerObserverViewer(TenantId), CancellationToken.None);
        var conversations = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<Conversation>>();
        var messages = scope.ServiceProvider.GetRequiredService<JsonDocumentStore<ChatMessage>>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditIntegrityService>();
        var member = await FirstEmployeeIdAsync(factory);
        var baseline = await GetGraphAsync(client, OwnerId);

        var denied = new List<(Conversation Conversation, string Marker)>();
        var matrix = new (string? Kind, string? ScopeType, string Marker)[]
        {
            ("Direct", "Direct", "secret-direct"),
            ("Direct", "Company", "secret-direct-kind"),
            ("Company", "Direct", "secret-direct-scope"),
            ("Position", "Topic", "secret-position-kind"),
            ("Topic", "Position", "secret-position-scope"),
            ("System", "Company", "secret-system-kind"),
            ("Company", "System", "secret-system-scope"),
            ("Unknown", "Company", "secret-unknown-kind"),
            ("Company", "Unknown", "secret-unknown-scope"),
            ("Company", null, "secret-null-scope"),
            (null, "Company", "secret-null-kind")
        };
        foreach (var (kind, scopeType, marker) in matrix)
        {
            var conversation = new Conversation($"conversation-{marker}", TenantId.ToString(), kind!, $"{marker}-title", "Active")
            {
                MemberEmployeeIds = [member], ScopeType = scopeType!, CreatedBy = "Agent", CreatorType = "Agent"
            };
            await conversations.PutAsync(conversation.Id, conversation, CancellationToken.None);
            await messages.PutAsync($"message-{marker}", new ChatMessage($"message-{marker}", TenantId.ToString(),
                conversation.Id, 1, "Agent", $"{marker}-body", "P2", DateTimeOffset.UtcNow), CancellationToken.None);
            await audit.AppendAsync(TenantId, "chat.message.sent", "Agent", new
            {
                message_id = $"message-{marker}", conversation_id = conversation.Id
            }, CancellationToken.None);
            denied.Add((conversation, marker));
        }
        var auditWithoutConversation = await audit.AppendAsync(TenantId, "chat.message.sent", "Agent", new
        {
            message_id = "secret-audit-without-conversation"
        }, CancellationToken.None);

        using var list = Request("/v1/conversations", OwnerId);
        using var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        foreach (var (conversation, marker) in denied)
        {
            Assert.DoesNotContain(conversation.Id, listJson, StringComparison.Ordinal);
            Assert.DoesNotContain(marker, listJson, StringComparison.Ordinal);
            using var read = Request($"/v1/conversations/{conversation.Id}/messages", OwnerId);
            using var readResponse = await client.SendAsync(read);
            Assert.Equal(HttpStatusCode.Forbidden, readResponse.StatusCode);
            Assert.DoesNotContain(marker, await readResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            using var trace = Request($"/v1/collaboration/trace?entityType=conversation&entityId={conversation.Id}", OwnerId);
            using var traceResponse = await client.SendAsync(trace);
            Assert.Equal(HttpStatusCode.OK, traceResponse.StatusCode);
            using var traceDocument = JsonDocument.Parse(await traceResponse.Content.ReadAsStringAsync());
            Assert.Empty(traceDocument.RootElement.GetProperty("activities").EnumerateArray());
        }

        using var graph = Request("/v1/collaboration/graph", OwnerId);
        using var graphResponse = await client.SendAsync(graph);
        Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
        var graphJson = await graphResponse.Content.ReadAsStringAsync();
        foreach (var (conversation, marker) in denied)
        {
            Assert.DoesNotContain(conversation.Id, graphJson, StringComparison.Ordinal);
            Assert.DoesNotContain(marker, graphJson, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("secret-audit-without-conversation", graphJson, StringComparison.Ordinal);
        Assert.DoesNotContain(auditWithoutConversation.Hash, graphJson, StringComparison.Ordinal);
        using var graphDocument = JsonDocument.Parse(graphJson);
        Assert.Equal(baseline.GetProperty("summary").GetProperty("conversationCount").GetInt32(),
            graphDocument.RootElement.GetProperty("summary").GetProperty("conversationCount").GetInt32());
        Assert.Equal(baseline.GetProperty("summary").GetProperty("messageCount").GetInt32(),
            graphDocument.RootElement.GetProperty("summary").GetProperty("messageCount").GetInt32());
        Assert.Equal(baseline.GetProperty("summary").GetProperty("activityCount").GetInt32(),
            graphDocument.RootElement.GetProperty("summary").GetProperty("activityCount").GetInt32());
    }

    [Fact]
    public async Task Employee_reader_remains_member_scoped_and_non_owner_without_employee_is_denied()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await AddMembershipAsync(factory, userId, "Member", "[\"chat.read\",\"collaboration.read\"]");
        await AddEmployeeAsync(factory, employeeId);
        using var scope = factory.Services.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        var participant = await chat.CreateConversationAsync(TenantId.ToString(), "Employee participant room", "Topic",
            [employeeId.ToString()], "Agent", employeeId.ToString(), "Topic", null, CancellationToken.None);
        var direct = await chat.CreateConversationAsync(TenantId.ToString(), "Employee direct room", "Direct",
            [employeeId.ToString()], "Agent", employeeId.ToString(), "Direct", null, CancellationToken.None);
        var outsider = await chat.CreateConversationAsync(TenantId.ToString(), "Employee outsider room", "Topic",
            [Guid.NewGuid().ToString()], "Agent", null, "Topic", null, CancellationToken.None);

        using var list = Request("/v1/conversations", userId, employeeId.ToString());
        using var listResponse = await client.SendAsync(list);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(participant.Id, listJson, StringComparison.Ordinal);
        Assert.Contains(direct.Id, listJson, StringComparison.Ordinal);
        Assert.DoesNotContain(outsider.Id, listJson, StringComparison.Ordinal);

        using var directMessages = Request($"/v1/conversations/{direct.Id}/messages", userId, employeeId.ToString());
        using var directResponse = await client.SendAsync(directMessages);
        Assert.Equal(HttpStatusCode.OK, directResponse.StatusCode);
        using var outsiderMessages = Request($"/v1/conversations/{outsider.Id}/messages", userId, employeeId.ToString());
        using var outsiderResponse = await client.SendAsync(outsiderMessages);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderResponse.StatusCode);

        using var noEmployee = Request("/v1/conversations", userId, capabilities: ["chat.read"]);
        using var noEmployeeResponse = await client.SendAsync(noEmployee);
        Assert.Equal(HttpStatusCode.Forbidden, noEmployeeResponse.StatusCode);
        using var noEmployeeGraph = Request("/v1/collaboration/graph", userId, capabilities: ["collaboration.read"]);
        using var noEmployeeGraphResponse = await client.SendAsync(noEmployeeGraph);
        Assert.Equal(HttpStatusCode.Forbidden, noEmployeeGraphResponse.StatusCode);
    }

    [Fact]
    public async Task Stale_owner_claims_do_not_survive_database_downgrade_or_membership_removal()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await AddMembershipAsync(factory, userId, "Owner", "[]");
        using var allowed = Request("/v1/conversations", userId);
        using var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        await SetMembershipAsync(factory, userId, "Member", "[]");
        using var downgraded = Request("/v1/conversations", userId);
        using var downgradedResponse = await client.SendAsync(downgraded);
        Assert.Equal(HttpStatusCode.Forbidden, downgradedResponse.StatusCode);

        await SetMembershipAsync(factory, userId, "Owner", "not-json");
        using var malformed = Request("/v1/conversations", userId);
        using var malformedResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.Forbidden, malformedResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Memberships.Remove(await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == userId));
            await db.SaveChangesAsync();
        }
        using var removed = Request("/v1/conversations", userId);
        using var removedResponse = await client.SendAsync(removed);
        Assert.Equal(HttpStatusCode.Forbidden, removedResponse.StatusCode);
    }

    [Theory]
    [InlineData("Owner", null, true, true, "observer")]
    [InlineData("CoOwner", null, true, true, "observer")]
    [InlineData("Member", "employee", true, true, "participant")]
    [InlineData("Member", "employee", true, false, "participant")]
    [InlineData("Member", null, false, false, "none")]
    [InlineData("Member", "invalid", false, false, "none")]
    public async Task Auth_me_reports_effective_access_from_current_authority(string role, string? employeeKind,
        bool canReadChat, bool canReadCollaboration, string scope)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await AddUserAsync(factory, userId);
        await AddMembershipAsync(factory, userId, role,
            canReadChat && canReadCollaboration ? "[\"chat.read\",\"collaboration.read\"]"
                : canReadChat ? "[\"chat.read\"]"
                : canReadCollaboration ? "[\"collaboration.read\"]" : "[]");
        await AddEmployeeAsync(factory, employeeId);
        var sessionId = await CreateSessionAsync(factory, userId);
        var employeeClaim = employeeKind switch
        {
            "employee" => employeeId.ToString(),
            "invalid" => "not-a-guid",
            _ => null
        };

        using var request = Request("/auth/me", userId, employeeClaim, sessionId: sessionId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var access = document.RootElement.GetProperty("effective_access");
        Assert.Equal(canReadChat, access.GetProperty("can_read_chat").GetBoolean());
        Assert.Equal(canReadCollaboration, access.GetProperty("can_read_collaboration").GetBoolean());
        Assert.Equal(scope, access.GetProperty("collaboration_scope").GetString());
        Assert.False(document.RootElement.TryGetProperty("employee_id", out _));
        Assert.False(document.RootElement.TryGetProperty("user_id", out _));
        Assert.False(document.RootElement.TryGetProperty("session_id", out _));
    }

    [Theory]
    [InlineData("Owner", "Emergency")]
    [InlineData("CoOwner", "Emergency")]
    [InlineData("Owner", "Restricted")]
    public async Task Auth_me_returns_no_observer_access_for_restricted_owner_sessions(string role, string sessionKind)
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid();
        await AddUserAsync(factory, userId);
        await AddMembershipAsync(factory, userId, role, "[\"chat.read\",\"collaboration.read\"]");
        var sessionId = await CreateSessionAsync(factory, userId, sessionKind,
            "[\"sso.restore\",\"account.access\"]");

        using var me = Request("/auth/me", userId, sessionId: sessionId);
        using var meResponse = await client.SendAsync(me);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        using var document = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        Assert.Equal(sessionKind, document.RootElement.GetProperty("session_kind").GetString());
        var access = document.RootElement.GetProperty("effective_access");
        Assert.False(access.GetProperty("can_read_chat").GetBoolean());
        Assert.False(access.GetProperty("can_read_collaboration").GetBoolean());
        Assert.Equal("none", access.GetProperty("collaboration_scope").GetString());

        using var conversations = Request("/v1/conversations", userId, sessionId: sessionId);
        using var conversationsResponse = await client.SendAsync(conversations);
        Assert.Equal(HttpStatusCode.Forbidden, conversationsResponse.StatusCode);
    }

    private static HttpRequestMessage Request(string path, Guid userId, string? employeeId = null,
        IReadOnlyList<string>? capabilities = null, Guid? sessionId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        TestIdentityRequest.Apply(request, TenantId, role: "Member", capabilities: capabilities ?? [],
            employeeId: employeeId, userId: userId, sessionId: sessionId);
        return request;
    }

    private static async Task<JsonElement> GetGraphAsync(HttpClient client, Guid userId)
    {
        using var request = Request("/v1/collaboration/graph", userId);
        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {payload}");
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    private static async Task AddUserAsync(ControlPlaneSecurityHttpFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Users.Add(new UserRecord
        {
            Id = userId, Email = $"user-{userId:N}@example.test", DisplayName = "Synthetic user"
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddMembershipAsync(ControlPlaneSecurityHttpFactory factory, Guid userId, string role,
        string capabilitiesJson)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId, Role = role,
            CapabilitiesJson = capabilitiesJson
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddEmployeeAsync(ControlPlaneSecurityHttpFactory factory, Guid employeeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = TenantId, EmployeeNo = $"EMP-{employeeId:N}",
            DisplayName = $"Synthetic employee {employeeId:N}", Position = "Engineering"
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, PositionId = Guid.NewGuid(),
            Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> CreateSessionAsync(ControlPlaneSecurityHttpFactory factory, Guid userId,
        string sessionKind = "Standard", string allowedActionsJson = "[]")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == userId);
        var capabilities = JsonSerializer.Deserialize<string[]>(membership.CapabilitiesJson) ?? [];
        var session = new AuthSessionRecord
        {
            Id = Guid.NewGuid(), UserId = userId, TenantId = TenantId, CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Status = "Active", SessionKind = sessionKind, AllowedActionsJson = allowedActionsJson,
            AuthorityHash = MembershipAuthority.ComputeAuthorityHash(membership.Role, capabilities)
        };
        await scope.ServiceProvider.GetRequiredService<JsonDocumentStore<AuthSessionRecord>>()
            .PutAsync(session.Id.ToString(), session, CancellationToken.None);
        return session.Id;
    }

    private static async Task SetMembershipAsync(ControlPlaneSecurityHttpFactory factory, Guid userId, string role,
        string capabilitiesJson)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var membership = await db.Memberships.SingleAsync(x => x.TenantId == TenantId && x.UserId == userId);
        membership.Role = role;
        membership.CapabilitiesJson = capabilitiesJson;
        await db.SaveChangesAsync();
    }

    private static async Task<string> FirstEmployeeIdAsync(ControlPlaneSecurityHttpFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        return (await db.Employees.Where(x => x.TenantId == TenantId).OrderBy(x => x.EmployeeNo).FirstAsync()).Id.ToString();
    }
}
