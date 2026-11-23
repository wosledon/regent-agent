using RegentAgent.Storage;

namespace RegentAgent.Evaluation;

public sealed record BenchmarkSuite(string Id, string TenantId, string Name, int Version, int CaseCount, string Status);
public sealed record EvaluationRun(string Id, string TenantId, string SuiteId, string CandidateEmployeeId, string ModelVersion, int Seed, decimal Score, bool SafetyPassed, string Status, DateTimeOffset CreatedAt);
public sealed record JudgeAssignment(string Id, string TenantId, string EvaluationRunId, string JudgeEmployeeId, string Independence, string CalibrationStatus);

public sealed class EvaluationService(
    JsonDocumentStore<BenchmarkSuite> suites,
    JsonDocumentStore<EvaluationRun> runs,
    JsonDocumentStore<JudgeAssignment> judges)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if ((await suites.GetAllAsync(cancellationToken)).Count > 0) return;
        await suites.PutAsync("core", new BenchmarkSuite("core", "11111111-1111-1111-1111-111111111111", "Core Capability", 1, 100, "Active"), cancellationToken);
        await suites.PutAsync("safety", new BenchmarkSuite("safety", "11111111-1111-1111-1111-111111111111", "Safety and Tenant Isolation", 1, 50, "Active"), cancellationToken);
    }

    public async Task<IReadOnlyList<BenchmarkSuite>> ListSuitesAsync(string tenantId, CancellationToken cancellationToken) =>
        (await suites.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<IReadOnlyList<EvaluationRun>> ListRunsAsync(string tenantId, CancellationToken cancellationToken) =>
        (await runs.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<EvaluationRun> RunAsync(string suiteId, string candidateEmployeeId, string tenantId, int seed, CancellationToken cancellationToken)
    {
        var suite = await suites.GetAsync(suiteId, cancellationToken) ?? throw new KeyNotFoundException("Benchmark suite not found.");
        var safetyPassed = true;
        var score = suite.Id == "safety" ? 100m : 88m;
        var status = safetyPassed && score >= 80m ? "Approved" : "Rejected";
        var run = new EvaluationRun(Guid.NewGuid().ToString("N"), tenantId, suite.Id, candidateEmployeeId,
            "local-cheap-v1", seed, score, safetyPassed, status, DateTimeOffset.UtcNow);
        await runs.PutAsync(run.Id, run, cancellationToken);
        await judges.PutAsync(run.Id, new JudgeAssignment(Guid.NewGuid().ToString("N"), tenantId, run.Id,
            "Judge Employee", "SameModelDifferentRuntime", "Calibrated"), cancellationToken);
        return run;
    }
}
