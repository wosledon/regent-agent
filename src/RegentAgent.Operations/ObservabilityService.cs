using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed class ObservabilityService(
    JsonDocumentStore<ObservabilityProfile> profiles,
    JsonDocumentStore<SloDefinition> slos,
    JsonDocumentStore<TelemetrySample> samples,
    JsonDocumentStore<AlertRecord> alerts,
    JsonDocumentStore<AlertSilenceRecord> silences,
    TimeProvider clock)
{
    public async Task<ObservabilityProfile> GetProfileAsync(CancellationToken cancellationToken) =>
        await profiles.GetAsync("default", cancellationToken) ?? new ObservabilityProfile();

    public async Task<ObservabilityProfile> UpdateProfileAsync(ObservabilityProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Mode is not ("off" or "local" or "otlp" or "elastic")) throw new InvalidOperationException("Invalid observability mode.");
        if (profile.MetricIntervalSeconds is < 5 or > 300) throw new InvalidOperationException("Metric interval must be between 5 and 300 seconds.");
        await profiles.PutAsync("default", profile, cancellationToken);
        return profile;
    }

    public async Task<SloDefinition> SaveSloAsync(SloDefinition slo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slo.Name) || string.IsNullOrWhiteSpace(slo.Metric)
            || slo.Target is < 0 or > 1 || slo.WindowDays is < 1 or > 366)
            throw new InvalidOperationException("Invalid SLO definition.");
        await slos.PutAsync(slo.Name, slo, cancellationToken);
        return slo;
    }

    public async Task RecordSampleAsync(TelemetrySample sample, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sample.Metric) || sample.Metric.Length > 128
            || sample.ServiceName.Length > 128
            || sample.TraceId?.Length > 128)
            throw new InvalidOperationException("Invalid telemetry sample.");
        sample.ObservedAt = clock.GetUtcNow();
        await samples.PutAsync(Guid.NewGuid().ToString("N"), sample, cancellationToken);
        await EvaluateAsync(cancellationToken);
    }

    public async Task RecordBrowserBatchAsync(BrowserTelemetryBatch batch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(batch.ServiceName) || batch.ServiceName.Length > 128
            || batch.Samples.Count is 0 or > 50)
            throw new InvalidOperationException("Invalid browser telemetry batch.");
        foreach (var sample in batch.Samples)
            await RecordSampleAsync(new TelemetrySample
            {
                Metric = sample.Metric, Value = sample.Value, Success = sample.Success,
                ServiceName = batch.ServiceName, TraceId = sample.TraceId
            }, cancellationToken);
    }

    public async Task<IReadOnlyList<SloEvaluationDto>> EvaluateAsync(CancellationToken cancellationToken)
    {
        var definitions = await slos.GetAllAsync(cancellationToken);
        var allSamples = await samples.GetAllAsync(cancellationToken);
        var evaluations = new List<SloEvaluationDto>();
        foreach (var slo in definitions)
        {
            var relevant = allSamples.Where(x => x.Metric == slo.Metric
                && x.ObservedAt >= clock.GetUtcNow().AddDays(-slo.WindowDays)).ToArray();
            var actual = relevant.Length == 0 ? 0m : (decimal)relevant.Count(x => x.Success) / relevant.Length;
            var allowedError = 1 - slo.Target;
            var observedError = 1 - actual;
            var remaining = allowedError <= 0 ? (observedError == 0 ? 1m : 0m) : Math.Max(0, (allowedError - observedError) / allowedError);
            var evaluation = new SloEvaluationDto(slo.Name, actual, slo.Target, actual >= slo.Target, remaining);
            var alertId = await SyncAlertAsync(slo, evaluation, relevant, cancellationToken);
            evaluations.Add(evaluation with { AlertId = alertId, Level = alertId is null ? "L0" : await AlertLevelAsync(alertId.Value, cancellationToken) });
        }
        return evaluations;
    }

    public async Task<IReadOnlyList<AlertRecord>> ListAlertsAsync(CancellationToken cancellationToken) =>
        (await alerts.GetAllAsync(cancellationToken)).OrderByDescending(item => item.UpdatedAt).ToArray();

    public async Task AcknowledgeAlertAsync(Guid id, AlertAcknowledgementRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Operator)) throw new InvalidOperationException("Operator is required.");
        var alert = await alerts.GetAsync(id.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Alert not found.");
        if (alert.Status == "Resolved") throw new InvalidOperationException("Resolved alerts cannot be acknowledged.");
        alert.Status = "Acknowledged";
        alert.AcknowledgedAt = clock.GetUtcNow();
        alert.AcknowledgedBy = request.Operator;
        alert.UpdatedAt = clock.GetUtcNow();
        await alerts.PutAsync(alert.Id.ToString(), alert, cancellationToken);
    }

    public async Task ResolveAlertAsync(Guid id, string resolution, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resolution)) throw new InvalidOperationException("Resolution is required.");
        var alert = await alerts.GetAsync(id.ToString(), cancellationToken) ?? throw new KeyNotFoundException("Alert not found.");
        alert.Status = "Resolved";
        alert.ResolvedAt = clock.GetUtcNow();
        alert.UpdatedAt = clock.GetUtcNow();
        alert.Message = resolution;
        await alerts.PutAsync(alert.Id.ToString(), alert, cancellationToken);
    }

    public async Task<AlertSilenceRecord> SilenceAlertAsync(AlertSilenceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Fingerprint) || string.IsNullOrWhiteSpace(request.Reason)
            || string.IsNullOrWhiteSpace(request.ApprovedBy) || request.Minutes is < 1 or > 1440)
            throw new InvalidOperationException("Silence requires a fingerprint, reason, approver and valid expiry.");
        var record = new AlertSilenceRecord
        {
            Fingerprint = request.Fingerprint, Reason = request.Reason, ApprovedBy = request.ApprovedBy,
            ExpiresAt = clock.GetUtcNow().AddMinutes(request.Minutes), CreatedAt = clock.GetUtcNow()
        };
        await silences.PutAsync(record.Id.ToString(), record, cancellationToken);
        return record;
    }

    private async Task<Guid?> SyncAlertAsync(SloDefinition slo, SloEvaluationDto evaluation,
        IReadOnlyList<TelemetrySample> samples, CancellationToken cancellationToken)
    {
        var tenant = samples.Select(x => x.TenantId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        var service = samples.Select(x => x.ServiceName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        var fingerprint = Fingerprint(tenant, service, slo.Name);
        var active = (await alerts.GetAllAsync(cancellationToken)).Where(item => item.Fingerprint == fingerprint
            && item.Status != "Resolved").ToArray();
        if (evaluation.Passed)
        {
            foreach (var alert in active)
            {
                alert.Status = "Resolved";
                alert.ResolvedAt = clock.GetUtcNow();
                alert.UpdatedAt = clock.GetUtcNow();
                await alerts.PutAsync(alert.Id.ToString(), alert, cancellationToken);
            }
            return null;
        }
        var level = evaluation.Actual == 0 && samples.Count >= 5 ? "L3"
            : evaluation.ErrorBudgetRemaining <= 0 ? "L3"
            : evaluation.ErrorBudgetRemaining <= 0.05m ? "L2" : "L1";
        var traceId = samples.LastOrDefault()?.TraceId;
        var silenced = (await silences.GetAllAsync(cancellationToken)).Any(item => item.Fingerprint == fingerprint
            && item.ExpiresAt > clock.GetUtcNow());
        if (active.Length > 0)
        {
            var alert = active[0];
            alert.Level = level;
            alert.Status = silenced ? "Silenced" : alert.Status == "Acknowledged" ? "Acknowledged" : "Firing";
            alert.Message = $"SLO {slo.Name} actual {evaluation.Actual:P2}, target {slo.Target:P2}; error budget {evaluation.ErrorBudgetRemaining:P1}.";
            alert.TraceId = traceId;
            alert.UpdatedAt = clock.GetUtcNow();
            await alerts.PutAsync(alert.Id.ToString(), alert, cancellationToken);
            return alert.Id;
        }
        var created = new AlertRecord
        {
            TenantId = tenant, ServiceName = service, SloName = slo.Name, Fingerprint = fingerprint,
            Level = level, Status = silenced ? "Silenced" : "Firing", TraceId = traceId,
            Message = $"SLO {slo.Name} actual {evaluation.Actual:P2}, target {slo.Target:P2}; error budget {evaluation.ErrorBudgetRemaining:P1}.",
            CreatedAt = clock.GetUtcNow(), UpdatedAt = clock.GetUtcNow()
        };
        await alerts.PutAsync(created.Id.ToString(), created, cancellationToken);
        return created.Id;
    }

    private async Task<string> AlertLevelAsync(Guid id, CancellationToken cancellationToken) =>
        (await alerts.GetAsync(id.ToString(), cancellationToken))?.Level ?? "L0";

    private static string Fingerprint(string tenant, string service, string slo) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { tenant, service, slo })))).ToLowerInvariant();
}
