using RegentAgent.Chat;
using RegentAgent.Evaluation;
using RegentAgent.Memory;
using RegentAgent.Storage;
using RegentAgent.Workflows;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class DomainModuleTests
{
    [Fact]
    public async Task Workflow_memory_chat_and_evaluation_run_locally()
    {
        var root = Path.Combine(Path.GetTempPath(), $"regent-domain-{Guid.NewGuid():N}");
        try
        {
            var workflows = new WorkflowService(
                new JsonDocumentStore<WorkflowDefinition>(Path.Combine(root, "workflows.json")),
                new JsonDocumentStore<WorkflowRun>(Path.Combine(root, "runs.json")));
            await workflows.SeedAsync(CancellationToken.None);
            var run = await workflows.StartAsync("delivery", "tenant", CancellationToken.None);
            Assert.NotNull(await workflows.AdvanceAsync(run.Id, CancellationToken.None));

            var memory = new MemoryService(
                new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "memory.json")),
                new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json")));
            await memory.SeedAsync(CancellationToken.None);
            var seeded = Assert.Single(await memory.SearchAsync("11111111-1111-1111-1111-111111111111", "Provider", CancellationToken.None));
            Assert.Equal(1, seeded.Version);

            var chat = new ChatService(
                new JsonDocumentStore<Conversation>(Path.Combine(root, "conversations.json")),
                new JsonDocumentStore<ChatMessage>(Path.Combine(root, "messages.json")));
            await chat.SeedAsync(CancellationToken.None);
            var sent = await chat.SendAsync("general", "11111111-1111-1111-1111-111111111111",
                "Tester", "Hello", "P2", CancellationToken.None);
            Assert.Equal(2, sent.Seq);

            var evaluation = new EvaluationService(
                new JsonDocumentStore<BenchmarkSuite>(Path.Combine(root, "suites.json")),
                new JsonDocumentStore<EvaluationRun>(Path.Combine(root, "evaluations.json")),
                new JsonDocumentStore<JudgeAssignment>(Path.Combine(root, "judges.json")));
            await evaluation.SeedAsync(CancellationToken.None);
            var result = await evaluation.RunAsync("core", "employee", "11111111-1111-1111-1111-111111111111", 42, CancellationToken.None);
            Assert.Equal("Approved", result.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
