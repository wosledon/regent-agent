using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Enums;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[CollectionDefinition("ControlPlane assignment tokens", DisableParallelization = true)]
public sealed class ControlPlaneAssignmentTokenCollection;

[Collection("ControlPlane assignment tokens")]
public sealed class AgentAssignmentRevocationTests
{
    [Fact]
    public async Task Termination_revokes_old_epoch_and_rehire_does_not_restore_it()
    {
        await using var factory = new ControlPlaneRuntimeFactory("Development", "InMemory", true);
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        Guid oldPeriodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "token", CompanyName = "Token Co" });
            db.Employees.Add(new EmployeeRecord
            {
                Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-TOKEN", DisplayName = "Employee",
                Position = "Engineering", State = EmployeeState.Active
            });
            oldPeriodId = Guid.NewGuid();
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = oldPeriodId, TenantId = tenantId, EmployeeId = employeeId,
                PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var oldToken = await IssueTokenAsync(client, tenantId, employeeId);
        using (var before = await GetIdentityAsync(client, oldToken))
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            (await db.Employees.SingleAsync(x => x.Id == employeeId)).State = EmployeeState.Terminated;
            (await db.EmploymentPeriods.SingleAsync(x => x.Id == oldPeriodId)).EndedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        using (var terminated = await GetIdentityAsync(client, oldToken))
            Assert.Equal(HttpStatusCode.Forbidden, terminated.StatusCode);

        Guid newPeriodId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            (await db.Employees.SingleAsync(x => x.Id == employeeId)).State = EmployeeState.Active;
            newPeriodId = Guid.NewGuid();
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = newPeriodId, TenantId = tenantId, EmployeeId = employeeId,
                PositionId = Guid.NewGuid(), Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }
        using (var staleAfterRehire = await GetIdentityAsync(client, oldToken))
            Assert.Equal(HttpStatusCode.Forbidden, staleAfterRehire.StatusCode);
        var newToken = await IssueTokenAsync(client, tenantId, employeeId);
        using (var current = await GetIdentityAsync(client, newToken))
        {
            Assert.Equal(HttpStatusCode.OK, current.StatusCode);
            using var identity = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
            Assert.Equal(newPeriodId, identity.RootElement.GetProperty("credentialEpoch").GetGuid());
        }
    }

    private static async Task<string> IssueTokenAsync(HttpClient client, Guid tenantId, Guid employeeId)
    {
        using var response = await client.PostAsJsonAsync("/auth/dev-agent-token", new
        {
            tenantId, employeeId, scopes = new[] { "infer:employee" }
        });
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> GetIdentityAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/agent/v1/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
