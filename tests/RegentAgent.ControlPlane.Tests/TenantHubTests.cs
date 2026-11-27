using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Realtime;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TenantHubTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid EmployeeId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Hub_requires_active_session_chat_read_and_conversation_membership()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-hub-{Guid.NewGuid():N}");
        try
        {
            var chat = CreateChat(root);
            var member = EmployeeId;
            var credentialEpoch = Guid.NewGuid();
            var conversation = await chat.CreateConversationAsync(TenantId.ToString(), "Private", "Topic",
                [member.ToString()], "Owner", member.ToString(), "Topic", null, CancellationToken.None);
            var groups = new Mock<IGroupManager>();
            groups.Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            groups.Setup(x => x.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var sessions = new Mock<ITenantSessionValidator>();
            sessions.Setup(x => x.ValidateAsync(SessionId, UserId, TenantId, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            using var services = CreateServices();
            var registry = CreateRegistry();
            var callerContext = new TestHubCallerContext(Principal(member.ToString(), credentialEpoch: credentialEpoch));
            var hub = new TenantHub(chat, sessions.Object, services.GetRequiredService<MembershipAuthority>(), registry)
            {
                Context = callerContext, Groups = groups.Object
            };

            await hub.OnConnectedAsync();
            Assert.Equal(0, callerContext.AbortCount);
            Assert.Equal("connection", Assert.Single(registry.Snapshot()).ConnectionId);
            await hub.JoinConversation(conversation.Id);
            await hub.LeaveConversation(conversation.Id);

            groups.Verify(x => x.AddToGroupAsync("connection", TenantId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(x => x.AddToGroupAsync("connection", TenantHub.UserGroup(TenantId.ToString(), UserId.ToString()),
                It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(x => x.AddToGroupAsync("connection", TenantHub.ConversationGroup(TenantId.ToString(), conversation.Id),
                It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(x => x.RemoveFromGroupAsync("connection", TenantHub.ConversationGroup(TenantId.ToString(), conversation.Id),
                It.IsAny<CancellationToken>()), Times.Once);

            await registry.DisconnectSessionAsync(SessionId, CancellationToken.None);
            Assert.Equal(1, callerContext.AbortCount);
            Assert.Empty(registry.Snapshot());

            var outsider = NewHub(chat, sessions.Object, services, registry,
                Principal(Guid.NewGuid().ToString(), credentialEpoch: credentialEpoch));
            await outsider.OnConnectedAsync();
            await Assert.ThrowsAsync<HubException>(() => outsider.JoinConversation(conversation.Id));
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
                (await db.Memberships.SingleAsync()).CapabilitiesJson = "[]";
                await db.SaveChangesAsync();
            }
            var noChatRead = NewHub(chat, sessions.Object, services, registry,
                Principal(member.ToString(), capabilities: [], credentialEpoch: credentialEpoch));
            await noChatRead.OnConnectedAsync();
            await Assert.ThrowsAsync<HubException>(() => noChatRead.JoinConversation(conversation.Id));
            var otherTenant = NewHub(chat, sessions.Object, services, registry,
                Principal(member.ToString(), Guid.NewGuid(), credentialEpoch: credentialEpoch));
            await Assert.ThrowsAsync<HubException>(() => otherTenant.OnConnectedAsync());

            sessions.Setup(x => x.ValidateAsync(SessionId, UserId, TenantId, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            await Assert.ThrowsAsync<HubException>(() => hub.JoinConversation(conversation.Id));
            Assert.Equal(2, callerContext.AbortCount);
            Assert.Empty(registry.Snapshot());
            await Assert.ThrowsAsync<HubException>(() => new TenantHub(chat, sessions.Object,
                services.GetRequiredService<MembershipAuthority>(), registry)
            {
                Context = new TestHubCallerContext(Principal(member.ToString(), credentialEpoch: credentialEpoch)),
                Groups = groups.Object
            }.OnConnectedAsync());
            sessions.Verify(x => x.ValidateAsync(SessionId, UserId, TenantId, member.ToString(), credentialEpoch,
                It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Hub_observer_join_uses_shared_scope_allowlist_and_denies_direct_chats()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tenant-hub-observer-{Guid.NewGuid():N}");
        try
        {
            var chat = CreateChat(root);
            var member = Guid.NewGuid().ToString();
            var shared = await chat.CreateConversationAsync(TenantId.ToString(), "Shared", "Company",
                [member], "Agent", member, "Company", null, CancellationToken.None);
            var direct = await chat.CreateConversationAsync(TenantId.ToString(), "Direct", "Direct",
                [member], "Agent", member, "Direct", null, CancellationToken.None);
            var groups = new Mock<IGroupManager>();
            groups.Setup(x => x.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var sessions = new Mock<ITenantSessionValidator>();
            sessions.Setup(x => x.ValidateAsync(SessionId, UserId, TenantId, It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            using var services = CreateServices();
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
                (await db.Memberships.SingleAsync()).Role = "Owner";
                (await db.Memberships.SingleAsync()).CapabilitiesJson = "[]";
                await db.SaveChangesAsync();
            }
            var callerContext = new TestHubCallerContext(Principal(null, capabilities: []));
            var hub = new TenantHub(chat, sessions.Object, services.GetRequiredService<MembershipAuthority>(),
                CreateRegistry())
            {
                Context = callerContext, Groups = groups.Object
            };

            await hub.OnConnectedAsync();
            await hub.JoinConversation(shared.Id);
            await Assert.ThrowsAsync<HubException>(() => hub.JoinConversation(direct.Id));

            groups.Verify(x => x.AddToGroupAsync("connection",
                TenantHub.ConversationGroup(TenantId.ToString(), shared.Id), It.IsAny<CancellationToken>()), Times.Once);
            groups.Verify(x => x.AddToGroupAsync("connection",
                TenantHub.ConversationGroup(TenantId.ToString(), direct.Id), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Revoking_a_session_aborts_registered_connections()
    {
        var terminationCount = 0;
        var registry = CreateRegistry();
        RegisterConnection(registry, "connection", TenantId, UserId, Guid.NewGuid().ToString(), () => terminationCount++);

        await registry.DisconnectSessionAsync(SessionId, CancellationToken.None);

        Assert.Equal(1, terminationCount);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task Failed_connection_termination_retains_registration_for_retry()
    {
        var terminationCount = 0;
        var registry = CreateRegistry();
        RegisterConnection(registry, "connection", TenantId, UserId, Guid.NewGuid().ToString(), () =>
        {
            terminationCount++;
            if (terminationCount == 1) throw new InvalidOperationException("Termination failed.");
        });

        await registry.DisconnectConnectionAsync("connection", CancellationToken.None);

        Assert.Equal(1, terminationCount);
        Assert.Equal("connection", Assert.Single(registry.Snapshot()).ConnectionId);

        await registry.DisconnectConnectionAsync("connection", CancellationToken.None);

        Assert.Equal(2, terminationCount);
        Assert.Empty(registry.Snapshot());
    }

    [Fact]
    public async Task Canceled_connection_termination_preserves_registration()
    {
        var terminationCount = 0;
        var registry = CreateRegistry();
        RegisterConnection(registry, "connection", TenantId, UserId, Guid.NewGuid().ToString(), () => terminationCount++);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.DisconnectConnectionAsync("connection", cancellation.Token));

        Assert.Equal(0, terminationCount);
        Assert.Equal("connection", Assert.Single(registry.Snapshot()).ConnectionId);
    }

    [Fact]
    public async Task Employee_disconnect_is_scoped_by_tenant_and_identity()
    {
        var registry = CreateRegistry();
        var employeeId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var terminated = new List<string>();
        RegisterConnection(registry, "employee", TenantId, UserId, employeeId.ToString(), () => terminated.Add("employee"));
        RegisterConnection(registry, "other-tenant", otherTenantId, UserId, employeeId.ToString(),
            () => terminated.Add("other-tenant"));
        RegisterConnection(registry, "other-employee", TenantId, UserId, otherEmployeeId.ToString(),
            () => terminated.Add("other-employee"));

        await registry.DisconnectEmployeeAsync(TenantId, employeeId, CancellationToken.None);

        Assert.Equal(new[] { "employee" }, terminated);
        Assert.Equal(new[] { "other-employee", "other-tenant" },
            registry.Snapshot().Select(connection => connection.ConnectionId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task User_disconnect_is_scoped_by_tenant_and_identity()
    {
        var registry = CreateRegistry();
        var otherUserId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var terminated = new List<string>();
        RegisterConnection(registry, "user", TenantId, UserId, Guid.NewGuid().ToString(), () => terminated.Add("user"));
        RegisterConnection(registry, "second-session", TenantId, UserId, Guid.NewGuid().ToString(),
            () => terminated.Add("second-session"));
        RegisterConnection(registry, "other-tenant", otherTenantId, UserId, Guid.NewGuid().ToString(),
            () => terminated.Add("other-tenant"));
        RegisterConnection(registry, "other-user", TenantId, otherUserId, Guid.NewGuid().ToString(),
            () => terminated.Add("other-user"));

        await registry.DisconnectUserAsync(TenantId, UserId, CancellationToken.None);

        terminated.Sort(StringComparer.Ordinal);
        Assert.Equal(new[] { "second-session", "user" }, terminated);
        Assert.Equal(new[] { "other-tenant", "other-user" },
            registry.Snapshot().Select(connection => connection.ConnectionId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Notifier_sends_full_message_to_conversation_and_id_free_invalidations_to_tenant()
    {
        var tenantId = TenantId.ToString();
        const string conversationId = "private-conversation";
        var message = new ChatMessage("message-1", tenantId, conversationId, 1, "Member", "Sensitive body",
            "P2", DateTimeOffset.UtcNow);
        var clients = new Mock<IHubClients>();
        var tenantProxy = new Mock<IClientProxy>();
        var conversationProxy = new Mock<IClientProxy>();
        var tenantPayloads = new Dictionary<string, object?>(StringComparer.Ordinal);
        tenantProxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((eventName, args, _) =>
                tenantPayloads.Add(eventName, Assert.Single(args)));
        clients.Setup(x => x.Group(TenantHub.TenantGroup(tenantId))).Returns(tenantProxy.Object);
        clients.Setup(x => x.Group(TenantHub.ConversationGroup(tenantId, conversationId))).Returns(conversationProxy.Object);
        var hub = new Mock<IHubContext<TenantHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var notifier = new TenantHubNotifier(hub.Object);
        var approvalId = Guid.NewGuid();
        var coordinationId = Guid.NewGuid();

        await notifier.MessageCreatedAsync(tenantId, message, CancellationToken.None);
        await notifier.ApprovalChangedAsync(tenantId, approvalId, CancellationToken.None);
        await notifier.CoordinationChangedAsync(tenantId, coordinationId, CancellationToken.None);

        conversationProxy.Verify(x => x.SendCoreAsync("message.created",
            It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], message)), It.IsAny<CancellationToken>()), Times.Once);
        tenantProxy.Verify(x => x.SendCoreAsync("message.created",
            It.Is<object?[]>(args => args.Any(value => value is ChatMessage)), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(3, tenantPayloads.Count);
        AssertTenantInvalidation(tenantPayloads, "message.created", "message", message.Id, message.ConversationId);
        AssertTenantInvalidation(tenantPayloads, "approval.changed", "approval", approvalId.ToString());
        AssertTenantInvalidation(tenantPayloads, "coordination.changed", "coordination", coordinationId.ToString());
    }

    private static void AssertTenantInvalidation(IReadOnlyDictionary<string, object?> payloads, string eventName,
        string resourceType, params string[] forbiddenIds)
    {
        var invalidation = Assert.IsType<TenantInvalidation>(payloads[eventName]);
        Assert.Equal(resourceType, invalidation.ResourceType);
        var payload = JsonSerializer.SerializeToElement(invalidation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new[] { "resourceType" }, payload.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(resourceType, payload.GetProperty("resourceType").GetString());
        foreach (var id in forbiddenIds)
            Assert.DoesNotContain(id, payload.GetRawText(), StringComparison.Ordinal);
    }

    private static TenantHub NewHub(ChatService chat, ITenantSessionValidator sessions, ServiceProvider services,
        TenantHubConnectionRegistry registry, ClaimsPrincipal principal) => new(chat, sessions,
        services.GetRequiredService<MembershipAuthority>(), registry)
    {
        Context = new TestHubCallerContext(principal), Groups = Mock.Of<IGroupManager>()
    };

    private static ServiceProvider CreateServices()
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RegentDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<MembershipAuthority>();
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Memberships.Add(new MembershipRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, Role = "Member",
            CapabilitiesJson = "[\"chat.read\"]"
        });
        db.Employees.Add(new EmployeeRecord
        {
            Id = EmployeeId, TenantId = TenantId, EmployeeNo = $"EMP-{EmployeeId:N}",
            DisplayName = "Member", Position = "Engineering"
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, PositionId = Guid.NewGuid(),
            Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
        return provider;
    }

    private static TenantHubConnectionRegistry CreateRegistry() => new();

    private static void RegisterConnection(TenantHubConnectionRegistry registry, string connectionId, Guid tenantId,
        Guid userId, string? employeeId, Action terminateConnection) =>
        registry.Connected(connectionId,
            new TenantHubConnection(SessionId, tenantId, userId, employeeId, terminateConnection));

    private static ClaimsPrincipal Principal(string? employeeId, Guid? tenantId = null, string[]? capabilities = null,
        Guid? credentialEpoch = null)
    {
        var claims = new List<Claim>
        {
            new("tenant_id", (tenantId ?? TenantId).ToString()),
            new(ClaimTypes.NameIdentifier, UserId.ToString()),
            new("session_id", SessionId.ToString()),
            new("auth_method", "Oidc")
        };
        if (employeeId is not null) claims.Add(new Claim("employee_id", employeeId));
        if (credentialEpoch is { } epoch) claims.Add(new Claim("credential_epoch", epoch.ToString()));
        claims.AddRange((capabilities ?? [ControlPlanePolicies.ChatReadCapability]).Select(value => new Claim("capability", value)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ChatService CreateChat(string root) => new(
        new JsonDocumentStore<Conversation>(Path.Combine(root, "conversations.json")),
        new JsonDocumentStore<ChatMessage>(Path.Combine(root, "messages.json")));

    private sealed class TestHubCallerContext : HubCallerContext
    {
        private readonly ClaimsPrincipal user;
        public TestHubCallerContext(ClaimsPrincipal user) => this.user = user;
        public override string ConnectionId => "connection";
        public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public int AbortCount { get; private set; }
        public override void Abort() => AbortCount++;
    }
}
