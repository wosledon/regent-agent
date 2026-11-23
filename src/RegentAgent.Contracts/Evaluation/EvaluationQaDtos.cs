namespace RegentAgent.Contracts.Evaluation;

public sealed class EvaluationCaseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SuiteId { get; set; } = string.Empty;
    public string Split { get; set; } = "Development";
    public string ContentHash { get; set; } = string.Empty;
    public double ContaminationScore { get; set; }
    public bool Sealed { get; set; }
}

public sealed class JudgeProfile
{
    public Guid EmployeeId { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string RuntimeId { get; set; } = string.Empty;
    public bool IndependentRuntime { get; set; }
}

public sealed record JudgeAssignmentResult(bool Accepted, string Independence, decimal Confidence, string Reason);

public sealed class CalibrationSample
{
    public Guid CaseId { get; set; }
    public bool JudgeAVerdict { get; set; }
    public bool JudgeBVerdict { get; set; }
}

public sealed class CalibrationReport
{
    public int SampleCount { get; set; }
    public decimal Agreement { get; set; }
    public decimal Kappa { get; set; }
    public int RequiredSampleCount { get; set; }
    public bool Passed { get; set; }
}

public sealed record WilsonInterval(decimal Low, decimal High);
public sealed record McNemarResult(int DiscordantA, int DiscordantB, decimal PValue);

public sealed class EvaluationQaGate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SuiteId { get; set; } = string.Empty;
    public Guid CandidateEmployeeId { get; set; }
    public int Repeats { get; set; }
    public int SampleCount { get; set; }
    public int ContaminatedCases { get; set; }
    public bool CalibrationPassed { get; set; }
    public WilsonInterval? ScoreInterval { get; set; }
    public McNemarResult? BaselineComparison { get; set; }
    public string Status { get; set; } = "Review";
    public string Reason { get; set; } = string.Empty;
}

public sealed class EvaluationQaState
{
    public Guid TenantId { get; set; }
    public List<EvaluationCaseRecord> Cases { get; set; } = [];
    public List<JudgeProfile> Judges { get; set; } = [];
    public List<CalibrationReport> Calibrations { get; set; } = [];
    public List<EvaluationQaGate> Gates { get; set; } = [];
}
