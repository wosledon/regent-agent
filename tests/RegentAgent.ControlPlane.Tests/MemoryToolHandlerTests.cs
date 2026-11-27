using System.Text.Json;
using RegentAgent.AgentIdentity;
using RegentAgent.ControlPlane.Application.AgentTools;
using RegentAgent.Memory;
using RegentAgent.Storage;
using Xunit;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;

namespace RegentAgent.ControlPlane.Tests;

public sealed class MemoryToolHandlerTests
{
    [Fact]
    public async Task Agent_memory_capture_and_search_are_scoped_to_the_employment_period()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memory-tool-epoch-{Guid.NewGuid():N}");
        try
        {
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var memory = new MemoryService(claims,
                new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json")));
            var handler = new MemoryToolHandler(memory);
            var tenantId = Guid.NewGuid();
            var employeeId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            var oldPeriodId = Guid.NewGuid();
            var newPeriodId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var oldContext = Context(tenantId, employeeId, taskId, oldPeriodId, now);
            var capture = JsonSerializer.SerializeToElement(new
            {
                operation = "capture", scope = "personal", content = "Old period private note", sourceRef = "source"
            });

            var captured = Assert.IsType<MemoryClaim>(await handler.ExecuteAsync(oldContext, capture, CancellationToken.None));

            Assert.Equal(oldPeriodId.ToString(), captured.AuthorEmploymentPeriodId);
            var search = JsonSerializer.SerializeToElement(new
            {
                operation = "search", scope = "personal", query = "Old period private note"
            });
            var newPeriodResults = Assert.IsAssignableFrom<IEnumerable<MemoryClaim>>(
                await handler.ExecuteAsync(Context(tenantId, employeeId, taskId, newPeriodId, now), search,
                    CancellationToken.None));
            Assert.Empty(newPeriodResults);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static AgentToolContext Context(Guid tenantId, Guid employeeId, Guid taskId, Guid periodId,
        DateTimeOffset now) => new(
        new AgentIdentityModel(tenantId, employeeId, taskId, "runner", ["memory.read", "memory.write"],
            Guid.NewGuid().ToString("N"), now, now.AddMinutes(15), periodId),
        "Employee", "{}", "Low", "{}");
}
