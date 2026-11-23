using RegentAgent.Contracts.Evaluation;
using RegentAgent.Storage;

namespace RegentAgent.Evaluation;

public sealed class EvaluationQaService(JsonDocumentStore<EvaluationQaState> store)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public Task<EvaluationQaState> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state => (false, state), cancellationToken);

    public Task<EvaluationCaseRecord> RegisterCaseAsync(Guid tenantId, EvaluationCaseRecord record, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
        {
            if (record.Split is not ("Development" or "Calibration" or "Holdout")) throw new InvalidOperationException("Invalid evaluation split.");
            record.ContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{record.SuiteId}:{record.Split}:{record.Id}"))).ToLowerInvariant();
            state.Cases.Add(record);
            return (true, record);
        }, cancellationToken);

    public Task<EvaluationCaseRecord> SealCaseAsync(Guid tenantId, Guid caseId, CancellationToken cancellationToken) =>
        MutateAsync(tenantId, state =>
    {
        var record = Find(state.Cases, caseId);
        record.Sealed = true;
        return (true, record);
    }, cancellationToken);

    public Task<EvaluationCaseRecord> AssessContaminationAsync(Guid tenantId, Guid caseId, double similarity,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (similarity is < 0 or > 1) throw new InvalidOperationException("Similarity must be in [0, 1].");
        var record = Find(state.Cases, caseId);
        record.ContaminationScore = similarity;
        return (true, record);
    }, cancellationToken);

    public Task<JudgeAssignmentResult> AssignJudgeAsync(Guid tenantId, Guid candidateEmployeeId, string candidateModel,
        string candidateRuntime, JudgeProfile judge, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        if (judge.EmployeeId == candidateEmployeeId) return (false, new JudgeAssignmentResult(false, "SelfJudge", 0m, "Candidate cannot judge itself."));
        if (!judge.IndependentRuntime || judge.RuntimeId == candidateRuntime)
            return (false, new JudgeAssignmentResult(false, "SharedRuntime", 0m, "Judge runtime must be independent."));
        var sameModel = judge.ModelVersion == candidateModel;
        var result = new JudgeAssignmentResult(true, sameModel ? "SameModelDifferentRuntime" : "DifferentModel",
            sameModel ? 0.70m : 0.95m, sameModel ? "Allowed with reduced confidence." : "Independent model and runtime.");
        state.Judges.Add(judge);
        return (true, result);
    }, cancellationToken);

    public Task<CalibrationReport> CalibrateAsync(Guid tenantId, string suiteId, int suiteSize, IReadOnlyList<CalibrationSample> samples,
        CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var required = Math.Max(100, (int)Math.Ceiling(suiteSize * 0.20));
        var agreement = samples.Count == 0 ? 0m : (decimal)samples.Count(x => x.JudgeAVerdict == x.JudgeBVerdict) / samples.Count;
        var positiveA = (decimal)samples.Count(x => x.JudgeAVerdict) / Math.Max(1, samples.Count);
        var positiveB = (decimal)samples.Count(x => x.JudgeBVerdict) / Math.Max(1, samples.Count);
        var expected = positiveA * positiveB + (1 - positiveA) * (1 - positiveB);
        var kappa = expected == 1m ? 1m : (agreement - expected) / (1 - expected);
        var report = new CalibrationReport
        {
            SampleCount = samples.Count, RequiredSampleCount = required, Agreement = agreement,
            Kappa = kappa, Passed = samples.Count >= required && agreement >= 0.85m && kappa >= 0.70m
        };
        state.Calibrations.Add(report);
        return (true, report);
    }, cancellationToken);

    public Task<EvaluationQaGate> EvaluateGateAsync(Guid tenantId, string suiteId, Guid candidateEmployeeId, int repeats,
        int sampleCount, int successes, int baselineSuccesses, int discordantA, int discordantB,
        bool calibrationPassed, CancellationToken cancellationToken) => MutateAsync(tenantId, state =>
    {
        var contaminated = state.Cases.Count(x => x.SuiteId == suiteId && x.ContaminationScore >= 0.92);
        var interval = Wilson(successes, sampleCount);
        var comparison = new McNemarResult(discordantA, discordantB, McNemarP(discordantA, discordantB));
        var gate = new EvaluationQaGate
        {
            SuiteId = suiteId, CandidateEmployeeId = candidateEmployeeId, Repeats = repeats,
            SampleCount = sampleCount, ContaminatedCases = contaminated,
            CalibrationPassed = calibrationPassed, ScoreInterval = interval, BaselineComparison = comparison
        };
        if (contaminated > 0)
        {
            gate.Status = "Rejected";
            gate.Reason = "Holdout contamination detected.";
        }
        else if (!calibrationPassed)
        {
            gate.Status = "Rejected";
            gate.Reason = "Judge calibration threshold failed.";
        }
        else if (repeats < 3 || sampleCount < 30)
        {
            gate.Status = "Review";
            gate.Reason = "Minimum repeat or sample threshold not met.";
        }
        else if (interval.Low < 0.80m || comparison.PValue < 0.05m && successes <= baselineSuccesses)
        {
            gate.Status = "Rejected";
            gate.Reason = "Quality or baseline comparison threshold failed.";
        }
        else
        {
            gate.Status = "Approved";
            gate.Reason = "Evaluation QA gate passed.";
        }
        state.Gates.Add(gate);
        return (true, gate);
    }, cancellationToken);

    public Task<EvaluationScheduleRecord> SaveScheduleAsync(Guid tenantId, EvaluationScheduleRecord schedule,
        CancellationToken cancellationToken)
    {
        if (schedule.Frequency is not ("Daily" or "Weekly" or "Monthly")) throw new InvalidOperationException("Invalid evaluation frequency.");
        schedule.TenantId = tenantId;
        schedule.NextRunAt = NextRun(schedule.Frequency, DateTimeOffset.UtcNow);
        return MutateAsync(tenantId, state =>
        {
            state.Schedules.RemoveAll(x => x.Id == schedule.Id);
            state.Schedules.Add(schedule);
            return (true, schedule);
        }, cancellationToken);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var states = await store.GetAllAsync(cancellationToken);
        var executed = 0;
        foreach (var state in states)
        {
            var due = state.Schedules.Where(x => x.Enabled && x.NextRunAt <= DateTimeOffset.UtcNow).ToArray();
            foreach (var schedule in due)
            {
                try
                {
                    await EvaluateGateAsync(state.TenantId, schedule.SuiteId, schedule.CandidateEmployeeId,
                        schedule.Repeats, schedule.SampleCount, schedule.Successes, schedule.BaselineSuccesses,
                        schedule.DiscordantA, schedule.DiscordantB, schedule.CalibrationPassed, cancellationToken);
                    schedule.LastRunAt = DateTimeOffset.UtcNow;
                    schedule.LastError = null;
                }
                catch (Exception exception)
                {
                    schedule.LastError = exception.Message;
                }
                schedule.NextRunAt = NextRun(schedule.Frequency, DateTimeOffset.UtcNow);
                await MutateAsync(state.TenantId, current =>
                {
                    current.Schedules.RemoveAll(x => x.Id == schedule.Id);
                    current.Schedules.Add(schedule);
                    return (true, true);
                }, cancellationToken);
                executed++;
            }
        }
        return executed;
    }

    private static DateTimeOffset NextRun(string frequency, DateTimeOffset now) => frequency switch
    {
        "Daily" => now.AddDays(1), "Weekly" => now.AddDays(7), "Monthly" => now.AddMonths(1), _ => now
    };

    private async Task<T> MutateAsync<T>(Guid tenantId, Func<EvaluationQaState, (bool Changed, T Result)> mutation,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var key = tenantId.ToString();
            var state = await store.GetAsync(key, cancellationToken) ?? new EvaluationQaState { TenantId = tenantId };
            if (state.TenantId != tenantId) throw new UnauthorizedAccessException("Evaluation QA state belongs to another tenant.");
            var (changed, result) = mutation(state);
            if (changed) await store.PutAsync(key, state, cancellationToken);
            return result;
        }
        finally { gate.Release(); }
    }

    private static T Find<T>(IEnumerable<T> items, Guid id) where T : class =>
        items.FirstOrDefault(x => (Guid?)typeof(T).GetProperty("Id")!.GetValue(x) == id)
        ?? throw new KeyNotFoundException($"{typeof(T).Name} not found.");

    private static WilsonInterval Wilson(int successes, int count)
    {
        if (count == 0) return new WilsonInterval(0m, 0m);
        var p = (double)successes / count;
        const double z = 1.96;
        var denominator = 1 + z * z / count;
        var center = (p + z * z / (2 * count)) / denominator;
        var margin = z * Math.Sqrt(p * (1 - p) / count + z * z / (4 * count * count)) / denominator;
        return new WilsonInterval((decimal)Math.Max(0, center - margin), (decimal)Math.Min(1, center + margin));
    }

    private static decimal McNemarP(int a, int b)
    {
        var n = a + b;
        if (n == 0) return 1m;
        var observed = Math.Min(a, b);
        double probability = 0;
        for (var i = 0; i <= observed; i++) probability += Binomial(n, i);
        return (decimal)Math.Min(1, probability * 2);
    }

    private static double Binomial(int n, int k)
    {
        double coefficient = 1;
        for (var i = 1; i <= k; i++) coefficient = coefficient * (n - i + 1) / i;
        return coefficient * Math.Pow(0.5, n);
    }
}
