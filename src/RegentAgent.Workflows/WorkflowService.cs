using RegentAgent.Storage;

namespace RegentAgent.Workflows;

public sealed record WorkflowDefinition(string Id, string TenantId, string Name, int Version, IReadOnlyList<string> Steps, string Status);
public sealed record WorkflowRun(string Id, string TenantId, string WorkflowId, int WorkflowVersion, int CurrentStep, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed class WorkflowService(JsonDocumentStore<WorkflowDefinition> definitions, JsonDocumentStore<WorkflowRun> runs)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await definitions.GetAllAsync(cancellationToken)).Count > 0) return;
        await definitions.PutAsync("delivery", new WorkflowDefinition("delivery", "11111111-1111-1111-1111-111111111111", "Product Delivery", 1,
            ["BriefReady", "Approved", "Building", "Verifying", "Releasing", "Live"], "Active"), cancellationToken);
        await definitions.PutAsync("hiring", new WorkflowDefinition("hiring", "11111111-1111-1111-1111-111111111111", "Hiring", 1,
            ["Screening", "Interview", "Assessment", "Decision"], "Active"), cancellationToken);
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken cancellationToken) => definitions.GetAllAsync(cancellationToken);
    public Task<WorkflowDefinition?> GetAsync(string id, CancellationToken cancellationToken) => definitions.GetAsync(id, cancellationToken);

    public async Task<WorkflowRun> StartAsync(string workflowId, string tenantId, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(workflowId, cancellationToken) ?? throw new KeyNotFoundException("Workflow not found.");
        var run = new WorkflowRun(Guid.NewGuid().ToString("N"), tenantId, definition.Id, definition.Version, 0, "Running", DateTimeOffset.UtcNow, null);
        await runs.PutAsync(run.Id, run, cancellationToken);
        return run;
    }

    public async Task<WorkflowRun?> AdvanceAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || run.Status != "Running") return null;
        var definition = await definitions.GetAsync(run.WorkflowId, cancellationToken);
        if (definition is null) return null;
        var next = run.CurrentStep + 1;
        var completed = next >= definition.Steps.Count;
        var updated = run with { CurrentStep = Math.Min(next, definition.Steps.Count - 1), Status = completed ? "Completed" : "Running", CompletedAt = completed ? DateTimeOffset.UtcNow : null };
        await runs.PutAsync(run.Id, updated, cancellationToken);
        return updated;
    }
}
