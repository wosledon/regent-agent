using RegentAgent.Contracts.Agents;
using RegentAgent.Storage;

namespace RegentAgent.Agents;

public sealed class AgentEvolutionService(JsonDocumentStore<AgentEvolutionState> store, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<PromptBundle> CreatePromptAsync(Guid tenantId, string name, string content, CancellationToken cancellationToken) =>
        CreatePromptAsync(tenantId, name, content, "Operations", cancellationToken);

    public Task<PromptBundle> CreatePromptAsync(Guid tenantId, string name, string content, string kind,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Prompt name and content are required.");
        if (kind is not ("Recruitment" or "Onboarding" or "Operations")) throw new InvalidOperationException("Invalid prompt kind.");
        var version = state.Prompts.Where(x => x.Name == name).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
        var prompt = new PromptBundle { TenantId = tenantId, Name = name, Content = content, Version = version, Kind = kind, CreatedAt = clock.GetUtcNow() };
        state.Prompts.Add(prompt);
        return (true, prompt);
    }, cancellationToken);

    public Task<PromptBundle> ActivatePromptAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var prompt = Find(state.Prompts, id);
            foreach (var item in state.Prompts.Where(x => x.Name == prompt.Name)) item.Status = "Inactive";
            prompt.Status = "Active";
            return (true, prompt);
        }, cancellationToken);

    public Task<SkillDefinition> CreateSkillAsync(Guid tenantId, string name, Guid promptBundleId, string configJson,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (state.Prompts.All(x => x.Id != promptBundleId)) throw new KeyNotFoundException("Prompt bundle not found.");
        var version = state.Skills.Where(x => x.Name == name).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
        var skill = new SkillDefinition { TenantId = tenantId, Name = name, Version = version, PromptBundleId = promptBundleId, ConfigJson = configJson };
        state.Skills.Add(skill);
        return (true, skill);
    }, cancellationToken);

    public Task<SkillDefinition> ActivateSkillAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            var skill = Find(state.Skills, id);
            foreach (var item in state.Skills.Where(x => x.Name == skill.Name)) item.Status = "Inactive";
            skill.Status = "Active";
            return (true, skill);
        }, cancellationToken);

    public Task<ToolDisclosurePolicy> SetToolPolicyAsync(Guid tenantId, ToolDisclosurePolicy policy,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (policy.Level is not ("Primary" or "Auxiliary" or "Restricted" or "Forbidden"))
            throw new InvalidOperationException("Invalid disclosure level.");
        state.ToolPolicies.RemoveAll(x => x.ToolName == policy.ToolName);
        state.ToolPolicies.Add(policy);
        return (true, policy);
    }, cancellationToken);

    public Task<ToolDisclosureResult> DiscloseAsync(Guid tenantId, ToolDisclosureRequest request,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var capabilities = request.Capabilities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tools = state.ToolPolicies.Where(x => x.Level != "Forbidden"
            && (x.Level != "Restricted" || request.RestrictedActivated)
            && (x.RequiredCapabilities.Count == 0 || x.RequiredCapabilities.Any(capabilities.Contains)))
            .Select(x => new DisclosedTool(x.ToolName, x.Level, x.Summary)).ToArray();
        return (false, new ToolDisclosureResult(tools, false));
    }, cancellationToken);

    public Task<OnboardingBaseline> CreateOnboardingBaselineAsync(Guid tenantId, Guid employeeId,
        Guid promptBundleId, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var prompt = state.Prompts.FirstOrDefault(x => x.Id == promptBundleId)
            ?? throw new KeyNotFoundException("Prompt bundle not found.");
        if (prompt.Status != "Active") throw new InvalidOperationException("Onboarding baseline requires an active prompt bundle.");
        if (employeeId == Guid.Empty) throw new InvalidOperationException("Employee is required.");
        var baseline = new OnboardingBaseline
        {
            TenantId = tenantId, EmployeeId = employeeId, PromptBundleId = promptBundleId,
            Status = "Active", CreatedAt = clock.GetUtcNow()
        };
        state.OnboardingBaselines.Add(baseline);
        return (true, baseline);
    }, cancellationToken);

    public Task<PublicAsset> PublishAssetAsync(Guid tenantId, string assetType, Guid assetId, string publisher,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (string.IsNullOrWhiteSpace(publisher)) throw new InvalidOperationException("Publisher is required.");
        int version;
        if (assetType == "Prompt")
        {
            var prompt = state.Prompts.FirstOrDefault(x => x.Id == assetId)
                ?? throw new KeyNotFoundException("Prompt bundle not found.");
            if (prompt.Status != "Active") throw new InvalidOperationException("Only active prompts can be published.");
            version = prompt.Version;
        }
        else if (assetType == "Skill")
        {
            var skill = state.Skills.FirstOrDefault(x => x.Id == assetId)
                ?? throw new KeyNotFoundException("Skill not found.");
            if (skill.Status != "Active") throw new InvalidOperationException("Only active skills can be published.");
            version = skill.Version;
        }
        else throw new InvalidOperationException("Public asset type must be Prompt or Skill.");
        if (state.PublicAssets.Any(x => x.AssetType == assetType && x.AssetId == assetId && x.Version == version))
            throw new InvalidOperationException("Asset version is already published.");
        var asset = new PublicAsset
        {
            TenantId = tenantId, AssetType = assetType, AssetId = assetId, Version = version,
            PublishedBy = publisher, PublishedAt = clock.GetUtcNow()
        };
        state.PublicAssets.Add(asset);
        return (true, asset);
    }, cancellationToken);

    public Task<AgentEvolutionState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    private async Task<T> MutateAsync<T>(Guid tenantId, Func<AgentEvolutionState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = tenantId.ToString();
            var state = await store.GetAsync(key, cancellationToken) ?? new AgentEvolutionState();
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(key, state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");
}
