using RegentAgent.Agents;
using RegentAgent.Contracts.Agents;
using RegentAgent.Memory;
using RegentAgent.Performance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class EvolutionServiceTests
{
    [Fact]
    public async Task Memory_forget_delete_and_tool_disclosure_are_enforced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"evolution-{Guid.NewGuid():N}");
        try
        {
            var tenantId = Guid.NewGuid().ToString();
            var claims = new JsonDocumentStore<MemoryClaim>(Path.Combine(root, "claims.json"));
            var memory = new MemoryService(claims,
                new JsonDocumentStore<CorrectionTask>(Path.Combine(root, "corrections.json")));
            await claims.PutAsync("claim-1", new MemoryClaim("claim-1", tenantId, "personal", "secret", "source", 0.9, "trusted", "Active", 1), CancellationToken.None);
            await memory.ForgetAsync("claim-1", tenantId, CancellationToken.None);
            Assert.Empty(await memory.SearchAsync(tenantId, null, CancellationToken.None));
            await memory.DeleteAsync("claim-1", tenantId, CancellationToken.None);
            Assert.Null(await claims.GetAsync("claim-1", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Recruitment_prompt_onboarding_and_public_assets_are_versioned()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-assets-{Guid.NewGuid():N}.json");
        try
        {
            var service = new AgentEvolutionService(new JsonDocumentStore<AgentEvolutionState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var employeeId = Guid.NewGuid();
            var prompt = await service.CreatePromptAsync(tenantId, "interview", "Run a blind job-related interview", "Recruitment", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateOnboardingBaselineAsync(tenantId, employeeId, prompt.Id, CancellationToken.None));
            await service.ActivatePromptAsync(tenantId, prompt.Id, CancellationToken.None);
            var baseline = await service.CreateOnboardingBaselineAsync(tenantId, employeeId, prompt.Id, CancellationToken.None);
            var asset = await service.PublishAssetAsync(tenantId, "Prompt", prompt.Id, "Owner", CancellationToken.None);

            Assert.Equal("Recruitment", prompt.Kind);
            Assert.Equal(employeeId, baseline.EmployeeId);
            Assert.Equal(1, asset.Version);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAssetAsync(tenantId, "Prompt", prompt.Id, "Owner", CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Tool_disclosure_requires_every_capability_and_fails_closed_for_invalid_policies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-disclosure-{Guid.NewGuid():N}.json");
        try
        {
            var tenantId = Guid.NewGuid();
            var store = new JsonDocumentStore<AgentEvolutionState>(path);
            await store.PutAsync(tenantId.ToString(), new AgentEvolutionState
            {
                ToolPolicies =
                [
                    new ToolDisclosurePolicy
                    {
                        ToolName = "multi", Level = "Primary", RequiredCapabilities = ["capability.a", "capability.b"]
                    },
                    new ToolDisclosurePolicy { ToolName = "", Level = "Primary" },
                    new ToolDisclosurePolicy
                    {
                        ToolName = "invalid", Level = "Primary", RequiredCapabilities = null!
                    }
                ]
            }, CancellationToken.None);
            var service = new AgentEvolutionService(store, TimeProvider.System);

            var partial = await service.DiscloseAsync(tenantId,
                new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "capability.a" }, false),
                CancellationToken.None);
            var complete = await service.DiscloseAsync(tenantId,
                new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "capability.a", "capability.b"
                }, false), CancellationToken.None);

            Assert.Empty(partial.Tools);
            Assert.Equal("multi", Assert.Single(complete.Tools).Name);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetToolPolicyAsync(tenantId,
                new ToolDisclosurePolicy { ToolName = "invalid", Level = "Primary", RequiredCapabilities = [" "] },
                CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Prompt_versions_and_restricted_tools_are_deterministic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agent-evolution-{Guid.NewGuid():N}.json");
        try
        {
            var service = new AgentEvolutionService(new JsonDocumentStore<AgentEvolutionState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var first = await service.CreatePromptAsync(tenantId, "review", "v1", CancellationToken.None);
            var second = await service.CreatePromptAsync(tenantId, "review", "v2", CancellationToken.None);
            await service.ActivatePromptAsync(tenantId, second.Id, CancellationToken.None);
            await service.SetToolPolicyAsync(tenantId, new ToolDisclosurePolicy { ToolName = "public", Level = "Primary" }, CancellationToken.None);
            await service.SetToolPolicyAsync(tenantId, new ToolDisclosurePolicy { ToolName = "admin", Level = "Restricted", RequiredCapabilities = ["org.update"] }, CancellationToken.None);
            await service.SetToolPolicyAsync(tenantId, new ToolDisclosurePolicy { ToolName = "secret", Level = "Forbidden" }, CancellationToken.None);
            var hidden = await service.DiscloseAsync(tenantId,
                new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "org.update" }, false), CancellationToken.None);
            var shown = await service.DiscloseAsync(tenantId,
                new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "org.update" }, true), CancellationToken.None);
            var withoutRequiredCapability = await service.DiscloseAsync(tenantId,
                new ToolDisclosureAuthority(new HashSet<string>(StringComparer.OrdinalIgnoreCase), true), CancellationToken.None);

            Assert.Equal(1, first.Version);
            Assert.Equal(2, second.Version);
            Assert.Single(hidden.Tools);
            Assert.Equal(2, shown.Tools.Count);
            Assert.Single(withoutRequiredCapability.Tools);
            Assert.DoesNotContain(shown.Tools, x => x.Name == "secret");
            Assert.False(shown.PermissionExpanded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
