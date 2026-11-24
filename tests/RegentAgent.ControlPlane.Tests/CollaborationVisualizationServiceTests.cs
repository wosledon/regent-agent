using Microsoft.EntityFrameworkCore;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Infrastructure;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class CollaborationVisualizationServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Collaboration_graph_combines_org_work_groups_messages_and_audit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"collaboration-graph-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            await DbInitializer.InitializeAsync(db, CancellationToken.None);
            var chat = CreateChat(db, root);
            var message = await chat.SendAsync("delivery-room", TenantId.ToString(), "Engineering Lead",
                "Checkpoint ready for review", "P1", CancellationToken.None,
                mentionedEmployeeIds: [db.Employees.First(x => x.Position == "Quality").Id.ToString()],
                messageType: "ReviewRequest", evidenceRefs: ["task:graph-review"], idempotencyKey: "graph-message-1");
            var repeated = await chat.SendAsync("delivery-room", TenantId.ToString(), "Engineering Lead",
                "Checkpoint ready for review", "P1", CancellationToken.None,
                mentionedEmployeeIds: [db.Employees.First(x => x.Position == "Quality").Id.ToString()],
                messageType: "ReviewRequest", evidenceRefs: ["task:graph-review"], idempotencyKey: "graph-message-1");
            Assert.Equal(message.Id, repeated.Id);
            await new AuditIntegrityService(db, TimeProvider.System).AppendAsync(TenantId, "chat.message.sent",
                "Engineering Lead", new { message_id = message.Id, conversation_id = "delivery-room" }, CancellationToken.None);
            var service = new CollaborationVisualizationService(db, chat, TimeProvider.System);

            var graph = await service.GetGraphAsync(TenantId, CancellationToken.None);
            var trace = await service.GetTraceAsync(TenantId, "conversation", "delivery-room", CancellationToken.None);

            Assert.Contains(graph.Nodes, x => x.Type == "Employee");
            Assert.Contains(graph.Nodes, x => x.Type == "Conversation" && x.ConversationId == "delivery-room");
            Assert.Contains(graph.Nodes, x => x.Type == "Project");
            Assert.Contains(graph.Edges, x => x.Kind == "reports_to");
            Assert.Contains(graph.Edges, x => x.Kind == "member_of");
            Assert.Contains(graph.Edges, x => x.Kind == "collaborates");
            Assert.Contains(graph.Edges, x => x.Kind == "works_on");
            Assert.Contains(graph.Activities, x => x.Type == "Message" && x.SourceId == message.Id);
            Assert.Contains(graph.Activities, x => x.Type == "Audit" && x.ConversationId == "delivery-room");
            Assert.Contains(trace.Activities, x => x.SourceId == message.Id);
            Assert.Contains(trace.Activities, x => x.Type == "Audit");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Conversation_reads_are_tenant_scoped_and_groups_require_members()
    {
        var root = Path.Combine(Path.GetTempPath(), $"collaboration-chat-{Guid.NewGuid():N}");
        try
        {
            await using var db = CreateDb();
            var chat = CreateChat(db, root);
            var otherTenant = Guid.NewGuid();
            var group = await chat.CreateConversationAsync(otherTenant.ToString(), "Other tenant", "Topic",
                [Guid.NewGuid().ToString()], "Owner", "Topic", null, CancellationToken.None);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chat.ListMessagesAsync(group.Id,
                TenantId.ToString(), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => chat.CreateConversationAsync(
                otherTenant.ToString(), "Empty", "Topic", [], "Owner", "Topic", null, CancellationToken.None));
            Assert.DoesNotContain(await chat.ListAsync(TenantId.ToString(), CancellationToken.None), x => x.Id == group.Id);
            var graph = await new CollaborationVisualizationService(db, chat, TimeProvider.System)
                .GetGraphAsync(otherTenant, CancellationToken.None);
            Assert.DoesNotContain(graph.Nodes, node => node.Type == "Employee" || node.Type == "Project");
            Assert.Contains(graph.Nodes, node => node.ConversationId == group.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static RegentDbContext CreateDb() => new(new DbContextOptionsBuilder<RegentDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ChatService CreateChat(RegentDbContext db, string root)
    {
        var chat = new ChatService(
            new JsonDocumentStore<Conversation>(Path.Combine(root, "conversations.json")),
            new JsonDocumentStore<ChatMessage>(Path.Combine(root, "messages.json")));
        var employees = db.Employees.AsNoTracking().Where(x => x.TenantId == TenantId)
            .Select(x => new ChatEmployee(x.Id.ToString(), x.DisplayName, x.Position)).ToArray();
        chat.SeedAsync(employees, CancellationToken.None).GetAwaiter().GetResult();
        return chat;
    }
}
