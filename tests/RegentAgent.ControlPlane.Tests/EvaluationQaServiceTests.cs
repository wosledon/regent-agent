using RegentAgent.Contracts.Evaluation;
using RegentAgent.Evaluation;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class EvaluationQaServiceTests
{
    [Fact]
    public async Task Calibration_statistics_and_contamination_gate_are_enforced()
    {
        var path = Path.Combine(Path.GetTempPath(), $"evaluation-qa-{Guid.NewGuid():N}.json");
        try
        {
            var service = new EvaluationQaService(new JsonDocumentStore<EvaluationQaState>(path));
            var tenantId = Guid.NewGuid();
            var record = await service.RegisterCaseAsync(tenantId, new EvaluationCaseRecord { SuiteId = "core", Split = "Holdout" }, CancellationToken.None);
            await service.SealCaseAsync(tenantId, record.Id, CancellationToken.None);
            var candidateId = Guid.NewGuid();
            var selfJudge = await service.AssignJudgeAsync(tenantId, candidateId, "model-a", "runtime-a", new JudgeProfile
            {
                EmployeeId = candidateId, ModelVersion = "model-a", RuntimeId = "runtime-b", IndependentRuntime = true
            }, CancellationToken.None);
            var samples = Enumerable.Range(0, 100).Select(i => new CalibrationSample
            {
                CaseId = Guid.NewGuid(), JudgeAVerdict = i < 90, JudgeBVerdict = i < 88
            }).ToArray();
            var calibration = await service.CalibrateAsync(tenantId, "core", 200, samples, CancellationToken.None);
            var approved = await service.EvaluateGateAsync(tenantId, "core", candidateId, 3, 100, 92, 85, 10, 2, calibration.Passed, CancellationToken.None);
            await service.AssessContaminationAsync(tenantId, record.Id, 0.92, CancellationToken.None);
            var contaminated = await service.EvaluateGateAsync(tenantId, "core", candidateId, 3, 100, 92, 85, 10, 2, calibration.Passed, CancellationToken.None);

            Assert.False(selfJudge.Accepted);
            Assert.True(calibration.Passed);
            Assert.Equal("Approved", approved.Status);
            Assert.Equal("Rejected", contaminated.Status);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
