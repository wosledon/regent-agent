using RegentAgent.Storage;
using RegentAgent.Workflows;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class WorkflowServiceTests
{
    [Fact]
    public async Task Personal_workflow_requires_independent_review_before_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-{Guid.NewGuid():N}");
        try
        {
            var service = new WorkflowService(
                new JsonDocumentStore<WorkflowDefinition>(Path.Combine(root, "definitions.json")),
                new JsonDocumentStore<WorkflowRun>(Path.Combine(root, "runs.json")));
            var personal = await service.CaptureAsync("tenant-a", "Release", ["Build", "Verify", "Release"], "Engineer", "position-1", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(personal.Id, "tenant-a", CancellationToken.None));

            var nominated = await service.NominateAsync(personal.Id, "tenant-a", CancellationToken.None);
            Assert.Equal(2, nominated.Version);
            Assert.Equal(personal.Id, nominated.ParentWorkflowId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(nominated.Id, "tenant-a", "Adopt", "Engineer", "Self review", CancellationToken.None));
            var adopted = await service.ReviewAsync(nominated.Id, "tenant-a", "Adopt", "Leader", "Evidence supports reuse", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(adopted.Id, "tenant-a", "Reject", "Leader", "Changed", CancellationToken.None));

            var run = await service.StartAsync(adopted.Id, "tenant-a", CancellationToken.None);
            var advanced = await service.AdvanceAsync(run.Id, CancellationToken.None);
            Assert.Equal(1, advanced!.CurrentStep);
            Assert.Equal("Running", advanced.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Workflow_list_and_start_are_tenant_scoped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workflow-scope-{Guid.NewGuid():N}");
        try
        {
            var service = new WorkflowService(
                new JsonDocumentStore<WorkflowDefinition>(Path.Combine(root, "definitions.json")),
                new JsonDocumentStore<WorkflowRun>(Path.Combine(root, "runs.json")));
            var workflow = await service.CaptureAsync("tenant-a", "Private", ["One", "Two"], "Engineer", "position-1", CancellationToken.None);

            Assert.Empty(await service.ListAsync("tenant-b", CancellationToken.None));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.StartAsync(workflow.Id, "tenant-b", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
