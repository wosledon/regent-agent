using RegentAgent.Contracts.Operations;
using RegentAgent.Storage;

namespace RegentAgent.Operations;

public sealed class ObservabilityService(
    JsonDocumentStore<ObservabilityProfile> profiles,
    JsonDocumentStore<SloDefinition> slos,
    JsonDocumentStore<TelemetrySample> samples,
    TimeProvider clock)
{
    public async Task<ObservabilityProfile> GetProfileAsync(CancellationToken cancellationToken) =>
        await profiles.GetAsync("default", cancellationToken) ?? new ObservabilityProfile();

    public async Task<ObservabilityProfile> UpdateProfileAsync(ObservabilityProfile profile, CancellationToken cancellationToken)
    {
        if (profile.Mode is not ("off" or "local" or "otlp" or "elastic")) throw new InvalidOperationException("Invalid observability mode.");
        await profiles.PutAsync("default", profile, cancellationToken);
        return profile;
    }

    public async Task<SloDefinition> SaveSloAsync(SloDefinition slo, CancellationToken cancellationToken)
    {
        if (slo.Target is < 0 or > 1 || slo.WindowDays <= 0) throw new InvalidOperationException("Invalid SLO definition.");
        await slos.PutAsync(slo.Name, slo, cancellationToken);
        return slo;
    }

    public async Task RecordSampleAsync(TelemetrySample sample, CancellationToken cancellationToken)
    {
        sample.ObservedAt = clock.GetUtcNow();
        await samples.PutAsync(Guid.NewGuid().ToString("N"), sample, cancellationToken);
    }

    public async Task<IReadOnlyList<SloEvaluationDto>> EvaluateAsync(CancellationToken cancellationToken)
    {
        var definitions = await slos.GetAllAsync(cancellationToken);
        var allSamples = await samples.GetAllAsync(cancellationToken);
        var evaluations = new List<SloEvaluationDto>();
        foreach (var slo in definitions)
        {
            var relevant = allSamples.Where(x => x.Metric == slo.Metric && x.ObservedAt >= clock.GetUtcNow().AddDays(-slo.WindowDays)).ToArray();
            var actual = relevant.Length == 0 ? 0m : (decimal)relevant.Count(x => x.Success) / relevant.Length;
            var allowedError = 1 - slo.Target;
            var observedError = 1 - actual;
            var remaining = allowedError <= 0 ? (observedError == 0 ? 1m : 0m) : Math.Max(0, (allowedError - observedError) / allowedError);
            evaluations.Add(new SloEvaluationDto(slo.Name, actual, slo.Target, actual >= slo.Target, remaining));
        }
        return evaluations;
    }
}
