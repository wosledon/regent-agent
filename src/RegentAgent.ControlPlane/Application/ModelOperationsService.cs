using RegentAgent.Contracts.Models;
using RegentAgent.Providers;
using RegentAgent.Storage;

namespace RegentAgent.ControlPlane.Application;

public sealed class ModelOperationsService(JsonDocumentStore<ModelPoolState> store, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task SeedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var key = tenantId.ToString();
        var state = await store.GetAsync(key, cancellationToken) ?? new ModelPoolState();
        if (state.Snapshots.Count > 0) return;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (snapshot, entries) = LocalModelCatalog.CreateSnapshot(today);
        state.Snapshots.Add(snapshot);
        state.Catalog.AddRange(entries);
        await store.PutAsync(key, state, cancellationToken);
    }

    public Task<ModelPoolState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    public Task<ModelPoolState> RefreshSnapshotAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        if (state.Snapshots.Any(x => x.SnapshotDate == date)) return (false, state);
        var (snapshot, entries) = LocalModelCatalog.CreateSnapshot(date);
        state.Snapshots.Add(snapshot);
        state.Catalog = entries.ToList();
        foreach (var price in state.ManualPrices)
        {
            var entry = state.Catalog.FirstOrDefault(x => x.Provider == price.Provider && x.ModelId == price.ModelId);
            if (entry is null) continue;
            entry.InputPricePerMillion = price.InputPricePerMillion;
            entry.OutputPricePerMillion = price.OutputPricePerMillion;
            entry.Currency = price.Currency;
            entry.PriceState = "Priced";
            entry.Source = "manual-price";
        }
        foreach (var assignment in state.TierAssignments)
        {
            var entry = state.Catalog.FirstOrDefault(x => x.Provider == assignment.Provider && x.ModelId == assignment.ModelId);
            if (entry is not null) entry.Tier = assignment.Tier;
        }
        return (true, state);
    }, cancellationToken);

    public Task<ModelCatalogEntry> AddModelAsync(Guid tenantId, ModelCatalogEntry entry,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(entry.Provider) || string.IsNullOrWhiteSpace(entry.ModelId)
            || string.IsNullOrWhiteSpace(entry.Version) || entry.Protocol is not ("OpenAI" or "Anthropic" or "Local"))
            throw new InvalidOperationException("Model identity, version and supported protocol are required.");
        if (state.Catalog.Any(x => x.Provider == entry.Provider && x.ModelId == entry.ModelId))
            throw new InvalidOperationException("Model already exists.");
        entry.Source = "manual-catalog";
        state.Catalog.Add(entry);
        return (true, entry);
    }, cancellationToken);

    public Task<ModelCatalogEntry> RotateModelAsync(Guid tenantId, string provider, string modelId,
        string version, string protocol, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var entry = FindModel(state, provider, modelId);
        if (string.IsNullOrWhiteSpace(version) || protocol is not ("OpenAI" or "Anthropic" or "Local"))
            throw new InvalidOperationException("Rotation version and supported protocol are required.");
        entry.Version = version;
        entry.Protocol = protocol;
        foreach (var binding in state.Bindings.Where(x => x.Provider == provider && x.ModelId == modelId && x.Status == "Active"))
            binding.Status = "RestoreRequired";
        return (true, entry);
    }, cancellationToken);

    public Task DeleteModelAsync(Guid tenantId, string provider, string modelId,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var entry = FindModel(state, provider, modelId);
        if (state.Bindings.Any(x => x.Provider == provider && x.ModelId == modelId))
            throw new InvalidOperationException("Cannot delete a model with employee bindings.");
        state.Catalog.Remove(entry);
        state.ManualPrices.RemoveAll(x => x.Provider == provider && x.ModelId == modelId);
        state.TierAssignments.RemoveAll(x => x.Provider == provider && x.ModelId == modelId);
        return (true, true);
    }, cancellationToken);

    public Task<ManualModelPrice> SetManualPriceAsync(Guid tenantId, ManualModelPrice price, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var existing = state.ManualPrices.FirstOrDefault(x => x.Provider == price.Provider && x.ModelId == price.ModelId);
            if (existing is not null) state.ManualPrices.Remove(existing);
            state.ManualPrices.Add(price);
            var entry = FindModel(state, price.Provider, price.ModelId);
            entry.InputPricePerMillion = price.InputPricePerMillion;
            entry.OutputPricePerMillion = price.OutputPricePerMillion;
            entry.Currency = price.Currency;
            entry.PriceState = "Priced";
            entry.Source = "manual-price";
            return (true, price);
        }, cancellationToken);

    public Task<ModelTierAssignment> AssignTierAsync(Guid tenantId, ModelTierAssignment assignment, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            if (assignment.Tier is not ("cheap" or "mid" or "premium")) throw new InvalidOperationException("Invalid model tier.");
            var existing = state.TierAssignments.FirstOrDefault(x => x.Provider == assignment.Provider && x.ModelId == assignment.ModelId);
            if (existing is not null)
            {
                existing.Tier = assignment.Tier;
                existing.Version++;
                assignment.Version = existing.Version;
            }
            else state.TierAssignments.Add(assignment);
            FindModel(state, assignment.Provider, assignment.ModelId).Tier = assignment.Tier;
            return (true, assignment);
        }, cancellationToken);

    public Task<EmployeeModelBinding> BindEmployeeAsync(Guid tenantId, EmployeeModelBinding binding, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var model = FindModel(state, binding.Provider, binding.ModelId);
            if (model.Version != binding.Version || model.Protocol != binding.Protocol)
                throw new InvalidOperationException("Model version or protocol does not match the catalog.");
            if (binding.Protocol is not ("OpenAI" or "Anthropic" or "Local"))
                throw new InvalidOperationException("Unsupported model protocol.");
            state.Bindings.RemoveAll(x => x.EmployeeId == binding.EmployeeId);
            state.Bindings.Add(binding);
            return (true, binding);
        }, cancellationToken);

    public Task<EmployeeModelBinding> RestoreBindingAsync(Guid tenantId, Guid employeeId, EmployeeModelBinding checkpoint,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var current = state.Bindings.SingleOrDefault(x => x.EmployeeId == employeeId)
            ?? throw new KeyNotFoundException("Employee binding not found.");
        if (current.Provider != checkpoint.Provider || current.ModelId != checkpoint.ModelId
            || current.Version != checkpoint.Version || current.ReasoningEffort != checkpoint.ReasoningEffort
            || current.Protocol != checkpoint.Protocol)
            throw new InvalidOperationException("Cross-model or cross-version restore is not allowed.");
        current.Status = "Active";
        return (true, current);
    }, cancellationToken);

    public Task<ModelUnavailableEvent?> ReportUnavailableAsync(Guid tenantId, Guid employeeId, string provider, string modelId,
        int consecutiveFailures, int unavailableMinutes, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (consecutiveFailures < 2 && unavailableMinutes < 5) return (false, (ModelUnavailableEvent?)null);
        var record = new ModelUnavailableEvent
        {
            EmployeeId = employeeId, Provider = provider, ModelId = modelId,
            ConsecutiveFailures = consecutiveFailures, UnavailableMinutes = unavailableMinutes,
            Escalation = consecutiveFailures >= 2 ? "Leader" : "Controller", DetectedAt = clock.GetUtcNow()
        };
        state.UnavailableEvents.Add(record);
        return (true, record);
    }, cancellationToken);

    public Task<ModelTestResult> TestModelAsync(Guid tenantId, string provider, string modelId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var model = FindModel(state, provider, modelId);
            var supported = model.Protocol is "OpenAI" or "Anthropic" or "Local";
            return (false, new ModelTestResult(provider, modelId, model.Protocol, supported,
                supported ? "Local contract test passed." : "Unsupported protocol."));
        }, cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId, Func<ModelPoolState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = tenantId.ToString();
            var state = await store.GetAsync(key, cancellationToken) ?? new ModelPoolState();
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(key, state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static ModelCatalogEntry FindModel(ModelPoolState state, string provider, string modelId) =>
        state.Catalog.FirstOrDefault(x => x.Provider == provider && x.ModelId == modelId)
        ?? throw new KeyNotFoundException("Model not found in catalog.");
}
