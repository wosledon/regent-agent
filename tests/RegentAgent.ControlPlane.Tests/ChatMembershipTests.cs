using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.Chat;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class ChatMembershipTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Service_lists_only_memberships_and_denies_non_members_and_cross_tenant_reads()
    {
        var root = Path.Combine(Path.GetTempPath(), $"chat-membership-{Guid.NewGuid():N}");
        try
        {
            var chat = CreateChat(root);
            var member = Guid.NewGuid().ToString();
            var outsider = Guid.NewGuid().ToString();
            var conversation = await chat.CreateConversationAsync(TenantId.ToString(), "Private", "Topic",
                [member], "Owner", member, "Topic", null, CancellationToken.None);
            var message = await chat.SendAsync(conversation.Id, TenantId.ToString(), "Member", "Membership scoped",
                "P2", CancellationToken.None, senderEmployeeId: member, actorType: "Agent", requireMembership: true);
            var otherTenant = Guid.NewGuid();
            var otherConversation = await chat.CreateConversationAsync(otherTenant.ToString(), "Other tenant", "Topic",
                [member], "Owner", member, "Topic", null, CancellationToken.None);

            Assert.Equal(conversation.Id, Assert.Single(await chat.ListAsync(TenantId.ToString(), CancellationToken.None, member)).Id);
            Assert.Empty(await chat.ListAsync(TenantId.ToString(), CancellationToken.None, outsider));
            Assert.Equal(message.Id, Assert.Single(await chat.ListMessagesAsync(conversation.Id, TenantId.ToString(),
                CancellationToken.None, member)).Id);
            Assert.False(await chat.IsMemberAsync(conversation.Id, TenantId.ToString(), outsider, CancellationToken.None));
            Assert.False(await chat.IsMemberAsync(otherConversation.Id, TenantId.ToString(), member, CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chat.ListMessagesAsync(conversation.Id,
                TenantId.ToString(), CancellationToken.None, outsider));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chat.ListMessagesAsync(otherConversation.Id,
                TenantId.ToString(), CancellationToken.None, member));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => chat.ListMessagesAsync(conversation.Id,
                TenantId.ToString(), CancellationToken.None, " "));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Human_endpoints_require_capability_employee_claim_and_conversation_membership()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        var member = Guid.NewGuid();
        var outsider = Guid.NewGuid();
        using (var databaseScope = factory.Services.CreateScope())
        {
            var db = databaseScope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Employees.AddRange(
                new EmployeeRecord
                {
                    Id = member, TenantId = TenantId, EmployeeNo = $"EMP-{member:N}",
                    DisplayName = $"Member {member:N}", Position = "Engineering"
                },
                new EmployeeRecord
                {
                    Id = outsider, TenantId = TenantId, EmployeeNo = $"EMP-{outsider:N}",
                    DisplayName = $"Outsider {outsider:N}", Position = "Engineering"
                });
            db.EmploymentPeriods.AddRange(
                new EmploymentPeriodRecord
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = member, PositionId = Guid.NewGuid(),
                    Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
                },
                new EmploymentPeriodRecord
                {
                    Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = outsider, PositionId = Guid.NewGuid(),
                    Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
                });
            await db.SaveChangesAsync();
        }
        var conversation = await chat.CreateConversationAsync(TenantId.ToString(), "Human private", "Topic",
            [member.ToString()], "Owner", member.ToString(), "Topic", null, CancellationToken.None);
        await chat.SendAsync(conversation.Id, TenantId.ToString(), "Member", "Only members can read this",
            "P2", CancellationToken.None, senderEmployeeId: member.ToString(), actorType: "Agent", requireMembership: true);
        var otherTenant = Guid.NewGuid();
        var otherConversation = await chat.CreateConversationAsync(otherTenant.ToString(), "Other tenant", "Topic",
            [member.ToString()], "Owner", member.ToString(), "Topic", null, CancellationToken.None);

        using var missingEmployee = new HttpRequestMessage(HttpMethod.Get, "/v1/conversations");
        TestIdentityRequest.Apply(missingEmployee, TenantId, capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var missingEmployeeResponse = await client.SendAsync(missingEmployee);
        Assert.Equal(HttpStatusCode.Forbidden, missingEmployeeResponse.StatusCode);

        using var malformedEmployee = new HttpRequestMessage(HttpMethod.Get, "/v1/conversations");
        TestIdentityRequest.Apply(malformedEmployee, TenantId, employeeId: "not-a-guid",
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var malformedEmployeeResponse = await client.SendAsync(malformedEmployee);
        Assert.Equal(HttpStatusCode.Forbidden, malformedEmployeeResponse.StatusCode);

        using var missingCapability = new HttpRequestMessage(HttpMethod.Get, "/v1/conversations");
        TestIdentityRequest.Apply(missingCapability, TenantId, employeeId: member.ToString());
        using var missingCapabilityResponse = await client.SendAsync(missingCapability);
        Assert.Equal(HttpStatusCode.Forbidden, missingCapabilityResponse.StatusCode);

        using var memberList = new HttpRequestMessage(HttpMethod.Get, "/v1/conversations");
        TestIdentityRequest.Apply(memberList, TenantId, employeeId: member.ToString(),
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var memberListResponse = await client.SendAsync(memberList);
        Assert.Equal(HttpStatusCode.OK, memberListResponse.StatusCode);
        Assert.Contains(conversation.Id, await memberListResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain(otherConversation.Id, await memberListResponse.Content.ReadAsStringAsync());

        using var outsiderList = new HttpRequestMessage(HttpMethod.Get, "/v1/conversations");
        TestIdentityRequest.Apply(outsiderList, TenantId, employeeId: outsider.ToString(),
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var outsiderListResponse = await client.SendAsync(outsiderList);
        Assert.Equal(HttpStatusCode.OK, outsiderListResponse.StatusCode);
        Assert.DoesNotContain(conversation.Id, await outsiderListResponse.Content.ReadAsStringAsync());

        using var memberMessages = new HttpRequestMessage(HttpMethod.Get, $"/v1/conversations/{conversation.Id}/messages");
        TestIdentityRequest.Apply(memberMessages, TenantId, employeeId: member.ToString(),
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var memberMessagesResponse = await client.SendAsync(memberMessages);
        Assert.Equal(HttpStatusCode.OK, memberMessagesResponse.StatusCode);
        Assert.Contains("Only members can read this", await memberMessagesResponse.Content.ReadAsStringAsync());

        using var outsiderMessages = new HttpRequestMessage(HttpMethod.Get, $"/v1/conversations/{conversation.Id}/messages");
        TestIdentityRequest.Apply(outsiderMessages, TenantId, employeeId: outsider.ToString(),
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var outsiderMessagesResponse = await client.SendAsync(outsiderMessages);
        Assert.Equal(HttpStatusCode.Forbidden, outsiderMessagesResponse.StatusCode);

        using var crossTenant = new HttpRequestMessage(HttpMethod.Get, $"/v1/conversations/{otherConversation.Id}/messages");
        TestIdentityRequest.Apply(crossTenant, TenantId, employeeId: member.ToString(),
            capabilities: [ControlPlanePolicies.ChatReadCapability]);
        using var crossTenantResponse = await client.SendAsync(crossTenant);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantResponse.StatusCode);
    }

    private static ChatService CreateChat(string root) => new(
        new JsonDocumentStore<Conversation>(Path.Combine(root, "conversations.json")),
        new JsonDocumentStore<ChatMessage>(Path.Combine(root, "messages.json")));
}
