using RegentAgent.Storage;

namespace RegentAgent.Workflows;

public sealed record WorkflowDefinition(string Id, string TenantId, string Name, int Version, IReadOnlyList<string> Steps,
    string Status, string? Author = null, string? ScopePositionId = null, string? ParentWorkflowId = null,
    string? ReviewedBy = null, string? ReviewNote = null, DateTimeOffset? CreatedAt = null, DateTimeOffset? ReviewedAt = null);
public sealed record WorkflowRun(string Id, string TenantId, string WorkflowId, int WorkflowVersion, int CurrentStep,
    string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed class WorkflowService(JsonDocumentStore<WorkflowDefinition> definitions, JsonDocumentStore<WorkflowRun> runs,
    TimeProvider? clock = null)
{
    private DateTimeOffset Now => clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await definitions.GetAllAsync(cancellationToken)).Count > 0) return;
        await definitions.PutAsync("delivery", new WorkflowDefinition("delivery", "11111111-1111-1111-1111-111111111111", "Product Delivery", 1,
            ["BriefReady", "Approved", "Building", "Verifying", "Releasing", "Live"], "Active", "system", CreatedAt: Now), cancellationToken);
        await definitions.PutAsync("hiring", new WorkflowDefinition("hiring", "11111111-1111-1111-1111-111111111111", "Hiring", 1,
            ["Screening", "Interview", "Assessment", "Decision"], "Active", "system", CreatedAt: Now), cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await definitions.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Version).ToArray();

    public async Task<WorkflowDefinition?> GetAsync(string id, string tenantId, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(id, cancellationToken);
        return definition is not null && definition.TenantId == tenantId ? definition : null;
    }

    public async Task<WorkflowDefinition> CaptureAsync(string tenantId, string name, IReadOnlyList<string> steps,
        string author, string scopePositionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(scopePositionId))
            throw new InvalidOperationException("Workflow name, author and scope are required.");
        if (steps.Count is < 2 or > 20 || steps.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Workflow requires 2 to 20 non-empty steps.");
        var definition = new WorkflowDefinition(Guid.NewGuid().ToString("N"), tenantId, name, 1, steps.ToArray(),
            "Personal", author, scopePositionId, CreatedAt: Now);
        await definitions.PutAsync(definition.Id, definition, cancellationToken);
        return definition;
    }

    public async Task<WorkflowDefinition> NominateAsync(string workflowId, string tenantId,
        CancellationToken cancellationToken)
    {
        var source = await definitions.GetAsync(workflowId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow not found.");
        if (source.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (source.Status != "Personal") throw new InvalidOperationException("Only personal workflows can be nominated.");
        var nominated = source with
        {
            Id = Guid.NewGuid().ToString("N"), Version = source.Version + 1, Status = "Nominated",
            ParentWorkflowId = source.Id, ReviewedBy = null, ReviewNote = null, ReviewedAt = null, CreatedAt = Now
        };
        await definitions.PutAsync(nominated.Id, nominated, cancellationToken);
        return nominated;
    }

    public async Task<WorkflowDefinition> ReviewAsync(string workflowId, string tenantId, string decision,
        string reviewer, string note, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(workflowId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow not found.");
        if (definition.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (definition.Status != "Nominated") throw new InvalidOperationException("Only nominated workflows can be reviewed.");
        if (decision is not ("Adopt" or "Pilot" or "Reject")) throw new InvalidOperationException("Invalid workflow review decision.");
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("Reviewer and note are required.");
        if (reviewer == definition.Author) throw new InvalidOperationException("Workflow author cannot review their own nomination.");
        var reviewed = definition with
        {
            Status = decision == "Adopt" ? "Adopted" : decision == "Pilot" ? "Pilot" : "Rejected",
            ReviewedBy = reviewer, ReviewNote = note, ReviewedAt = Now
        };
        await definitions.PutAsync(reviewed.Id, reviewed, cancellationToken);
        return reviewed;
    }

    public async Task<WorkflowRun> StartAsync(string workflowId, string tenantId, CancellationToken cancellationToken)
    {
        var definition = await definitions.GetAsync(workflowId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow not found.");
        if (definition.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (definition.Status is not ("Active" or "Adopted" or "Pilot"))
            throw new InvalidOperationException("Workflow is not approved for execution.");
        var run = new WorkflowRun(Guid.NewGuid().ToString("N"), tenantId, definition.Id, definition.Version, 0, "Running", Now, null);
        await runs.PutAsync(run.Id, run, cancellationToken);
        return run;
    }

    public async Task<WorkflowRun?> AdvanceAsync(string runId, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || run.Status != "Running") return null;
        var definition = await definitions.GetAsync(run.WorkflowId, cancellationToken);
        if (definition is null || definition.TenantId != run.TenantId || definition.Version != run.WorkflowVersion) return null;
        var next = run.CurrentStep + 1;
        var completed = next >= definition.Steps.Count;
        var updated = run with
        {
            CurrentStep = Math.Min(next, definition.Steps.Count - 1),
            Status = completed ? "Completed" : "Running", CompletedAt = completed ? Now : null
        };
        await runs.PutAsync(run.Id, updated, cancellationToken);
        return updated;
    }
}
