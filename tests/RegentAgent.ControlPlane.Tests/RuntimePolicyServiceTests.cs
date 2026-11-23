using RegentAgent.ControlPlane.Application;
using RegentAgent.Contracts.Models;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class RuntimePolicyServiceTests
{
    [Fact]
    public async Task Model_pool_enforces_binding_and_unavailability_rules()
    {
        var path = Path.Combine(Path.GetTempPath(), $"models-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ModelOperationsService(new JsonDocumentStore<ModelPoolState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            await service.SeedAsync(tenantId, CancellationToken.None);
            await service.SetManualPriceAsync(tenantId, new ManualModelPrice
            {
                Provider = "OpenAI", ModelId = "gpt-5-mini", InputPricePerMillion = 0.25m,
                OutputPricePerMillion = 2m, Currency = "USD", Reason = "Local manual price"
            }, CancellationToken.None);
            var binding = new EmployeeModelBinding
            {
                EmployeeId = Guid.NewGuid(), Provider = "OpenAI", ModelId = "gpt-5-mini",
                Version = "2026-11", Protocol = "OpenAI", ReasoningEffort = "medium"
            };
            await service.BindEmployeeAsync(tenantId, binding, CancellationToken.None);
            var wrong = new EmployeeModelBinding
            {
                EmployeeId = binding.EmployeeId, Provider = "OpenAI", ModelId = "gpt-5",
                Version = "2026-11", Protocol = "OpenAI", ReasoningEffort = "medium"
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreBindingAsync(tenantId, binding.EmployeeId, wrong, CancellationToken.None));
            var unavailable = await service.ReportUnavailableAsync(tenantId, binding.EmployeeId, "OpenAI", "gpt-5-mini", 2, 1, CancellationToken.None);
            Assert.NotNull(unavailable);
            Assert.Equal("Leader", unavailable!.Escalation);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Model_catalog_supports_add_rotate_and_safe_delete()
    {
        var path = Path.Combine(Path.GetTempPath(), $"model-lifecycle-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ModelOperationsService(new JsonDocumentStore<ModelPoolState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            await service.SeedAsync(tenantId, CancellationToken.None);
            var added = await service.AddModelAsync(tenantId, new ModelCatalogEntry
            {
                Provider = "OpenAI", ModelId = "custom-model", Version = "1", Protocol = "OpenAI"
            }, CancellationToken.None);
            Assert.Equal("manual-catalog", added.Source);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddModelAsync(tenantId, added, CancellationToken.None));
            var employeeId = Guid.NewGuid();
            await service.BindEmployeeAsync(tenantId, new EmployeeModelBinding
            {
                EmployeeId = employeeId, Provider = "OpenAI", ModelId = "custom-model", Version = "1", Protocol = "OpenAI"
            }, CancellationToken.None);
            await service.RotateModelAsync(tenantId, "OpenAI", "custom-model", "2", "OpenAI", CancellationToken.None);
            Assert.Equal("RestoreRequired", (await service.GetAsync(tenantId, CancellationToken.None)).Bindings.Single().Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteModelAsync(tenantId, "OpenAI", "custom-model", CancellationToken.None));

            var temporary = await service.AddModelAsync(tenantId, new ModelCatalogEntry
            {
                Provider = "Anthropic", ModelId = "temporary", Version = "1", Protocol = "Anthropic"
            }, CancellationToken.None);
            await service.DeleteModelAsync(tenantId, temporary.Provider, temporary.ModelId, CancellationToken.None);
            Assert.DoesNotContain((await service.GetAsync(tenantId, CancellationToken.None)).Catalog,
                model => model.ModelId == "temporary");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Costs_are_idempotent_currency_safe_and_schedule_aware()
    {
        var path = Path.Combine(Path.GetTempPath(), $"runtime-{Guid.NewGuid():N}.json");
        try
        {
            var service = new RuntimePolicyService(new JsonDocumentStore<RuntimePolicyState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var cost = new CostEntry { SourceEventId = "usage-1", Amount = 12.5m, Currency = "USD", Category = "Model" };
            var first = await service.RecordCostAsync(tenantId, cost, CancellationToken.None);
            var repeated = await service.RecordCostAsync(tenantId, cost, CancellationToken.None);
            await service.RecordCostAsync(tenantId, new CostEntry { SourceEventId = "usage-2", Amount = 88m, Currency = "CNY" }, CancellationToken.None);
            await service.UpdateScheduleAsync(tenantId, new WorkSchedule
            {
                TimeZone = "Asia/Shanghai", AlwaysAvailable = false,
                Windows = [new WorkWindow { Days = "Mon,Tue,Wed,Thu,Fri", StartLocal = "09:00", EndLocal = "17:00", Kind = "Silent" }]
            }, CancellationToken.None);
            var silent = await service.DecideDispatchAsync(tenantId, new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), false, CancellationToken.None);
            var emergency = await service.DecideDispatchAsync(tenantId, new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero), true, CancellationToken.None);
            var summary = await service.SummaryAsync(tenantId, null, CancellationToken.None);

            Assert.Equal(first.Id, repeated.Id);
            Assert.Equal(2, summary.Totals.Count);
            Assert.False(silent.RunNow);
            Assert.True(emergency.RunNow);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
