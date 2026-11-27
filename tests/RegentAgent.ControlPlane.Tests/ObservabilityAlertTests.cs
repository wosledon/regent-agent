using RegentAgent.Contracts.Operations;
using RegentAgent.Observability;
using RegentAgent.Operations;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class ObservabilityAlertTests
{
    [Fact]
    public async Task Slo_failure_deduplicates_acknowledges_and_recovers()
    {
        var root = NewRoot();
        try
        {
            var service = CreateService(root);
            await service.SaveSloAsync(new SloDefinition { Name = "api", Metric = "api.request", Target = 0.75m }, CancellationToken.None);
            for (var i = 0; i < 5; i++)
                await service.RecordSampleAsync(new TelemetrySample { Metric = "api.request", Success = false, ServiceName = "tenant-dashboard" }, CancellationToken.None);
            var failed = Assert.Single(await service.EvaluateAsync(CancellationToken.None));
            Assert.False(failed.Passed);
            Assert.Equal("L3", failed.Level);
            Assert.NotNull(failed.AlertId);
            var alert = Assert.Single(await service.ListAlertsAsync(CancellationToken.None));
            await service.AcknowledgeAlertAsync(alert.Id, new AlertAcknowledgementRequest("SRE"), CancellationToken.None);
            Assert.Equal("Acknowledged", (await service.ListAlertsAsync(CancellationToken.None))[0].Status);
            for (var i = 0; i < 20; i++)
                await service.RecordSampleAsync(new TelemetrySample { Metric = "api.request", Success = true, ServiceName = "tenant-dashboard" }, CancellationToken.None);
            var recovered = Assert.Single(await service.EvaluateAsync(CancellationToken.None));
            Assert.True(recovered.Passed);
            Assert.Null(recovered.AlertId);
            Assert.Equal("Resolved", (await service.ListAlertsAsync(CancellationToken.None))[0].Status);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Approved_silence_suppresses_new_alerts_until_expiry()
    {
        var root = NewRoot();
        try
        {
            var service = CreateService(root);
            await service.SaveSloAsync(new SloDefinition { Name = "lcp", Metric = "web.vital.lcp", Target = 0.95m }, CancellationToken.None);
            await service.RecordSampleAsync(new TelemetrySample { Metric = "web.vital.lcp", Success = false, ServiceName = "tenant-dashboard" }, CancellationToken.None);
            var alert = Assert.Single(await service.ListAlertsAsync(CancellationToken.None));
            await service.ResolveAlertAsync(alert.Id, "Test reset", CancellationToken.None);
            await service.SilenceAlertAsync(new AlertSilenceRequest(alert.Fingerprint, "Approved maintenance", 60), "SRE", CancellationToken.None);
            await service.RecordSampleAsync(new TelemetrySample { Metric = "web.vital.lcp", Success = false, ServiceName = "tenant-dashboard" }, CancellationToken.None);
            var alerts = await service.ListAlertsAsync(CancellationToken.None);
            Assert.Equal(2, alerts.Count);
            Assert.Equal("Silenced", alerts[0].Status);
        }
        finally { Delete(root); }
    }

    [Fact]
    public async Task Profile_reflects_runtime_observability_configuration()
    {
        var root = NewRoot();
        try
        {
            var service = new ObservabilityService(
                new JsonDocumentStore<ObservabilityProfile>(Path.Combine(root, "profile.json")),
                new JsonDocumentStore<SloDefinition>(Path.Combine(root, "slos.json")),
                new JsonDocumentStore<TelemetrySample>(Path.Combine(root, "samples.json")),
                new JsonDocumentStore<AlertRecord>(Path.Combine(root, "alerts.json")),
                new JsonDocumentStore<AlertSilenceRecord>(Path.Combine(root, "silences.json")),
                TimeProvider.System,
                new ObservabilityOptions { Profile = "elastic", ServiceName = "regent-operations" });

            var profile = await service.GetProfileAsync(CancellationToken.None);

            Assert.Equal("elastic", profile.Mode);
            Assert.Equal("regent-operations", profile.ServiceName);
            Assert.True(profile.ElasticEnabled);
        }
        finally { Delete(root); }
    }

    private static ObservabilityService CreateService(string root) => new(
        new JsonDocumentStore<ObservabilityProfile>(Path.Combine(root, "profile.json")),
        new JsonDocumentStore<SloDefinition>(Path.Combine(root, "slos.json")),
        new JsonDocumentStore<TelemetrySample>(Path.Combine(root, "samples.json")),
        new JsonDocumentStore<AlertRecord>(Path.Combine(root, "alerts.json")),
        new JsonDocumentStore<AlertSilenceRecord>(Path.Combine(root, "silences.json")),
        TimeProvider.System);

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"alerts-{Guid.NewGuid():N}");
    private static void Delete(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
