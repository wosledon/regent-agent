using RegentAgent.Contracts.Governance;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed record BuiltInToolDefinition(string Name, string Version, string Capability);

public sealed class AgentToolCatalog(GovernanceService governance)
{
    public static readonly IReadOnlyList<BuiltInToolDefinition> Definitions =
    [
        new("chat.send", "1.0.0", "chat:write"),
        new("chat.create_group", "1.0.0", "chat:write"),
        new("chat.add_member", "1.0.0", "chat:write"),
        new("task.checkpoint", "1.0.0", "tasks:execute"),
        new("task.complete", "1.0.0", "tasks:execute"),
        new("collaboration.delegate", "1.0.0", "tasks:execute"),
        new("collaboration.handoff", "1.0.0", "tasks:execute"),
        new("collaboration.help", "1.0.0", "tasks:execute"),
        new("collaboration.interrupt", "1.0.0", "tasks:execute"),
        new("memory.capture", "1.0.0", "memory.write"),
        new("memory.search", "1.0.0", "memory.read"),
        new("workflow.capture", "1.0.0", "workflow.capture"),
        new("workflow.nominate", "1.0.0", "workflow.capture"),
        new("workflow.start", "1.0.0", "workflow.capture"),
        new("project.create", "1.0.0", "project.decide"),
        new("project.add_finding", "1.0.0", "research.execute"),
        new("project.create_requirement", "1.0.0", "product.define"),
        new("project.claim_task", "1.0.0", "tasks:execute"),
        new("project.submit_task", "1.0.0", "tasks:execute"),
        new("organization.create_position", "1.0.0", "org.design"),
        new("improvement.propose", "1.0.0", "improvement.propose"),
        new("recruitment.create_candidate", "1.0.0", "recruitment.execute"),
        new("recruitment.assess", "1.0.0", "recruitment.execute"),
        new("recruitment.reject", "1.0.0", "recruitment.execute"),
        new("recruitment.retry", "1.0.0", "recruitment.execute"),
        new("recruitment.interview", "1.0.0", "recruitment.execute"),
        new("recruitment.fairness_audit", "1.0.0", "recruitment.execute"),
        new("performance.review", "1.0.0", "performance.review"),
        new("performance.action", "1.0.0", "performance.action"),
        new("performance.improvement_review", "1.0.0", "performance.review"),
        new("evaluation.run", "1.0.0", "evaluation.execute"),
        new("evaluation.qa_gate", "1.0.0", "evaluation.judge"),
        new("agent_evolution.create_prompt", "1.0.0", "agent.evolve"),
        new("agent_evolution.create_skill", "1.0.0", "agent.evolve"),
        new("agent_evolution.activate_prompt", "1.0.0", "agent.evolve"),
        new("agent_evolution.activate_skill", "1.0.0", "agent.evolve"),
        new("agent_evolution.onboarding", "1.0.0", "agent.evolve"),
        new("agent_evolution.publish_asset", "1.0.0", "agent.evolve"),
        new("task_governance.record_work", "1.0.0", "tasks:execute"),
        new("task_governance.create_review", "1.0.0", "acceptance.review"),
        new("task_governance.approve_review", "1.0.0", "acceptance.review"),
        new("task_governance.create_debate", "1.0.0", "technical.review"),
        new("task_governance.decide_debate", "1.0.0", "technical.review"),
        new("task_governance.adjudicate", "1.0.0", "project.decide")
    ];

    private readonly GovernanceService governance = governance;

    public async Task EnsureRegisteredAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var state = await governance.GetAsync(tenantId, cancellationToken);
        foreach (var definition in Definitions)
        {
            if (state.Tools.Any(x => x.Name == definition.Name && x.Version == definition.Version)) continue;
            var tool = await governance.RegisterToolAsync(tenantId, new ToolManifest
            {
                Name = definition.Name, Version = definition.Version,
                Capabilities = [definition.Capability], SbomJson = "{\"vulnerabilities\":[]}"
            }, cancellationToken);
            await governance.AdmitToolAsync(tenantId, tool.Id, "system-builtin", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ToolManifest>> DiscoverAsync(Guid tenantId, IReadOnlyList<string> scopes,
        CancellationToken cancellationToken)
    {
        await EnsureRegisteredAsync(tenantId, cancellationToken);
        var state = await governance.GetAsync(tenantId, cancellationToken);
        return state.Tools.Where(tool => tool.Status == "Admitted"
            && tool.Capabilities.Any(capability => scopes.Contains(capability, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(tool => tool.Name).ToArray();
    }
}
