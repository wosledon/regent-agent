using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Organization;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class OrganizationService(RegentDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<CharterDto?> GetCharterAsync(CancellationToken cancellationToken)
    {
        var charter = await db.Charters.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId.Value, cancellationToken);
        return charter is null ? null : ToCharter(charter);
    }

    public async Task<CharterDto> UpdateCharterAsync(CharterDto input, CancellationToken cancellationToken)
    {
        var id = tenant.TenantId.Value;
        var charter = await db.Charters.SingleOrDefaultAsync(x => x.TenantId == id, cancellationToken);
        if (charter is null)
        {
            charter = new CharterRecord { TenantId = id };
            db.Charters.Add(charter);
        }
        charter.Mission = input.Mission;
        charter.ConstraintsJson = JsonSerializer.Serialize(input.Constraints);
        charter.MonthlyBudgetMinor = input.MonthlyBudgetMinor;
        charter.Currency = input.Currency;
        charter.GovernanceJson = JsonSerializer.Serialize(input.Governance);
        charter.Version += 1;
        charter.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return ToCharter(charter);
    }

    public async Task<EmployeeSummary> StartCompanyAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var controllerPosition = await db.Positions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Name == "Controller", cancellationToken);
        if (controllerPosition is null)
        {
            controllerPosition = new PositionRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Name = "Controller",
                Responsibilities = "Plan the organization and coordinate delivery.",
                CapabilitiesJson = JsonSerializer.Serialize(new[] { "org.design", "project.decide", "task.assign" }),
                BudgetMinor = 1_000_000, Status = "Filled"
            };
            db.Positions.Add(controllerPosition);
        }

        var controller = await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.DisplayName == "Controller", cancellationToken);
        if (controller is null)
        {
            controller = new EmployeeRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = "EMP-0001",
                DisplayName = "Controller", Position = "Controller", State = EmployeeState.Active
            };
            db.Employees.Add(controller);
        }
        if (!await db.EmploymentPeriods.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == controller.Id && x.EndedAt == null, cancellationToken))
        {
            db.EmploymentPeriods.Add(new EmploymentPeriodRecord
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = controller.Id,
                PositionId = controllerPosition.Id, Kind = "Primary", StartedAt = clock.GetUtcNow()
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Mappings.ToSummary(controller, Freshness.Fresh);
    }

    public async Task<IReadOnlyList<PositionDto>> ListPositionsAsync(CancellationToken cancellationToken)
    {
        var items = await db.Positions.AsNoTracking().Where(x => x.TenantId == tenant.TenantId.Value).ToListAsync(cancellationToken);
        return items.Select(ToPosition).ToArray();
    }

    public async Task<PositionDto> CreatePositionAsync(PositionDto input, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        if (input.ParentPositionId is { } parentId)
        {
            var parent = await db.Positions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == parentId, cancellationToken)
                ?? throw new KeyNotFoundException("Parent position not found.");
            if (await GetDepthAsync(parent, tenantId, cancellationToken) >= 4)
                throw new InvalidOperationException("Organization depth cannot exceed 4.");
        }
        var record = new PositionRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = input.Name,
            Responsibilities = input.Responsibilities, CapabilitiesJson = JsonSerializer.Serialize(input.Capabilities),
            BudgetMinor = input.BudgetMinor, ParentPositionId = input.ParentPositionId, Status = "Open"
        };
        db.Positions.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToPosition(record);
    }

    public async Task<EmploymentPeriodDto> AssignAsync(Guid employeeId, Guid positionId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee not found.");
        var position = await db.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == positionId, cancellationToken)
            ?? throw new KeyNotFoundException("Position not found.");
        var current = await db.EmploymentPeriods.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Kind == "Primary" && x.EndedAt == null, cancellationToken);
        if (current is not null) current.EndedAt = clock.GetUtcNow();
        var period = new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            PositionId = positionId, Kind = "Primary", StartedAt = clock.GetUtcNow()
        };
        db.EmploymentPeriods.Add(period);
        employee.Position = position.Name;
        await db.SaveChangesAsync(cancellationToken);
        return ToPeriod(period);
    }

    public async Task<OrgDelegationDto> DelegateAsync(Guid delegateEmployeeId, Guid subtreePositionId,
        IReadOnlyList<string> actions, long budgetLimitMinor, int minutes, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var record = new OrgDelegationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId,
            DelegatorEmployeeId = Guid.Empty, DelegateEmployeeId = delegateEmployeeId,
            SubtreePositionId = subtreePositionId, ActionsJson = JsonSerializer.Serialize(actions),
            BudgetLimitMinor = budgetLimitMinor, ExpiresAt = clock.GetUtcNow().AddMinutes(minutes)
        };
        db.OrgDelegations.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToDelegation(record);
    }

    public async Task RevokeDelegationAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await db.OrgDelegations.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId.Value && x.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Delegation not found.");
        record.Status = "Revoked";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task OffboardAsync(Guid employeeId, string approvalId, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == Guid.Parse(approvalId), cancellationToken);
        if (approval is null || approval.State != ApprovalState.Approved || approval.ActionType != "employee.offboard")
            throw new InvalidOperationException("Human Boss approval is required.");
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken)
            ?? throw new KeyNotFoundException("Employee not found.");
        employee.State = EmployeeState.Terminated;
        employee.StateReason = "Offboarded after Boss approval";
        var periods = await db.EmploymentPeriods.Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.EndedAt == null).ToListAsync(cancellationToken);
        foreach (var period in periods) period.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CandidateDto> CreateCandidateAsync(Guid positionId, string displayName, string modelVersion,
        CancellationToken cancellationToken)
    {
        var candidate = new CandidateRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId.Value, PositionId = positionId,
            DisplayName = displayName, ModelVersion = modelVersion, Status = "Screening", CreatedAt = clock.GetUtcNow()
        };
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(cancellationToken);
        return ToCandidate(candidate);
    }

    public async Task<CandidateDto> AssessCandidateAsync(Guid candidateId, CandidateAssessmentDto assessment,
        CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId.Value && x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Candidate not found.");
        candidate.AssessmentJson = JsonSerializer.Serialize(assessment);
        candidate.FairnessJson = JsonSerializer.Serialize(new
        {
            job_related_only = true,
            identity_blind = true,
            slices = new[] { "provider", "model_version", "language" },
            sample_sufficient = true
        });
        candidate.Status = "Assessed";
        await db.SaveChangesAsync(cancellationToken);
        return ToCandidate(candidate);
    }

    public async Task<EmployeeSummary> HireCandidateAsync(Guid candidateId, string approvalId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var candidate = await db.Candidates.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Candidate not found.");
        var approval = await db.Approvals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == Guid.Parse(approvalId), cancellationToken);
        if (approval is null || approval.State != ApprovalState.Approved || approval.ActionType != "hiring.hire" || !approval.Title.Contains(candidate.DisplayName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Human Boss approval is required for hiring.");
        if (await db.Employees.CountAsync(x => x.TenantId == tenantId && x.State != EmployeeState.Terminated, cancellationToken) >= 20)
            throw new InvalidOperationException("Headcount limit reached.");
        var number = await db.Employees.CountAsync(x => x.TenantId == tenantId, cancellationToken) + 1;
        var employee = new EmployeeRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = $"EMP-{number:D4}",
            DisplayName = candidate.DisplayName, Position = "Candidate", State = EmployeeState.Active
        };
        db.Employees.Add(employee);
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id,
            PositionId = candidate.PositionId, Kind = "Primary", StartedAt = clock.GetUtcNow()
        });
        candidate.Status = "Hired";
        await db.SaveChangesAsync(cancellationToken);
        return Mappings.ToSummary(employee, Freshness.Fresh);
    }

    public async Task<PositionDto> SetPositionStatusAsync(Guid positionId, string status,
        CancellationToken cancellationToken)
    {
        if (status is not ("Open" or "Filled" or "Frozen" or "Closed")) throw new InvalidOperationException("Invalid position status.");
        var tenantId = tenant.TenantId.Value;
        var position = await db.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == positionId, cancellationToken)
            ?? throw new KeyNotFoundException("Position not found.");
        position.Status = status;
        position.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return ToPosition(position);
    }

    public async Task<PositionDto> MergePositionsAsync(Guid sourceId, Guid targetId,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var source = await db.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == sourceId, cancellationToken)
            ?? throw new KeyNotFoundException("Source position not found.");
        var target = await db.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == targetId, cancellationToken)
            ?? throw new KeyNotFoundException("Target position not found.");
        if (source.Id == target.Id) throw new InvalidOperationException("Cannot merge a position into itself.");
        var periods = await db.EmploymentPeriods.Where(x => x.TenantId == tenantId && x.PositionId == sourceId && x.EndedAt == null).ToListAsync(cancellationToken);
        foreach (var period in periods) period.PositionId = targetId;
        var employees = await db.Employees.Where(x => x.TenantId == tenantId && x.Position == source.Name).ToListAsync(cancellationToken);
        foreach (var employee in employees) employee.Position = target.Name;
        source.Status = "Closed";
        source.Version++;
        target.Status = "Filled";
        target.Version++;
        await db.SaveChangesAsync(cancellationToken);
        return ToPosition(target);
    }

    public async Task<PositionDto> SplitPositionAsync(Guid positionId, string name, string responsibilities,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var source = await db.Positions.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == positionId, cancellationToken)
            ?? throw new KeyNotFoundException("Position not found.");
        var split = new PositionRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name, Responsibilities = responsibilities,
            CapabilitiesJson = source.CapabilitiesJson, BudgetMinor = source.BudgetMinor / 2,
            ParentPositionId = source.ParentPositionId, Status = "Open"
        };
        source.BudgetMinor -= split.BudgetMinor;
        source.Version++;
        db.Positions.Add(split);
        await db.SaveChangesAsync(cancellationToken);
        return ToPosition(split);
    }

    public async Task<IReadOnlyList<InterviewStageDto>> ListInterviewStagesAsync(Guid? candidateId,
        CancellationToken cancellationToken)
    {
        var query = db.InterviewStages.AsNoTracking().Where(x => x.TenantId == tenant.TenantId.Value);
        if (candidateId is { } id) query = query.Where(x => x.CandidateId == id);
        return (await query.ToListAsync(cancellationToken)).Select(ToStageDto).ToArray();
    }

    public async Task<IReadOnlyList<CandidateAppealDto>> ListCandidateAppealsAsync(CancellationToken cancellationToken) =>
        (await db.CandidateAppeals.AsNoTracking().Where(x => x.TenantId == tenant.TenantId.Value).ToListAsync(cancellationToken)).Select(ToAppealDto).ToArray();

    public async Task<IReadOnlyList<FairnessAuditDto>> ListFairnessAuditsAsync(CancellationToken cancellationToken) =>
        (await db.FairnessAudits.AsNoTracking().Where(x => x.TenantId == tenant.TenantId.Value).ToListAsync(cancellationToken)).Select(ToFairnessDto).ToArray();

    public async Task<InterviewStageDto> RecordInterviewStageAsync(InterviewStageDto stage,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        if (!await db.Candidates.AnyAsync(x => x.TenantId == tenantId && x.Id == stage.CandidateId, cancellationToken))
            throw new KeyNotFoundException("Candidate not found.");
        if (stage.Stage is not ("Screening" or "Written" or "Practical" or "Interview" or "Offer"))
            throw new InvalidOperationException("Invalid interview stage.");
        var record = new Data.InterviewStageRecord
        {
            Id = stage.Id, TenantId = tenantId, CandidateId = stage.CandidateId, Stage = stage.Stage,
            Status = stage.Status, Score = stage.Score, EvidenceJson = stage.EvidenceJson, CreatedAt = clock.GetUtcNow()
        };
        db.InterviewStages.Add(record);
        var candidate = await db.Candidates.SingleAsync(x => x.TenantId == tenantId && x.Id == stage.CandidateId, cancellationToken);
        candidate.Status = stage.Stage == "Offer" ? "Offered" : "Interviewing";
        await db.SaveChangesAsync(cancellationToken);
        return ToStageDto(record);
    }

    public async Task<CandidateAppealDto> AppealCandidateAsync(CandidateAppealDto appeal,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        if (!await db.Candidates.AnyAsync(x => x.TenantId == tenantId && x.Id == appeal.CandidateId, cancellationToken))
            throw new KeyNotFoundException("Candidate not found.");
        var record = new Data.CandidateAppealRecord
        {
            Id = appeal.Id, TenantId = tenantId, CandidateId = appeal.CandidateId,
            Reason = appeal.Reason, Status = "Open"
        };
        db.CandidateAppeals.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToAppealDto(record);
    }

    public async Task<CandidateAppealDto> ResolveCandidateAppealAsync(Guid appealId, string resolution,
        CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var appeal = await db.CandidateAppeals.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == appealId, cancellationToken)
            ?? throw new KeyNotFoundException("Appeal not found.");
        appeal.Status = "Resolved";
        appeal.Resolution = resolution;
        await db.SaveChangesAsync(cancellationToken);
        return ToAppealDto(appeal);
    }

    public async Task<FairnessAuditDto> RunFairnessAuditAsync(Guid positionId, string period,
        int sampleSize, decimal selectionRateRatio, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        if (!await db.Positions.AnyAsync(x => x.TenantId == tenantId && x.Id == positionId, cancellationToken))
            throw new KeyNotFoundException("Position not found.");
        if (sampleSize <= 0 || selectionRateRatio is < 0 or > 1) throw new InvalidOperationException("Fairness sample is invalid.");
        var record = new Data.FairnessAuditRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PositionId = positionId, Period = period,
            SampleSize = sampleSize, SelectionRateRatio = selectionRateRatio,
            Status = selectionRateRatio >= 0.8m ? "Passed" : "ReviewRequired",
            DetailsJson = JsonSerializer.Serialize(new { job_related = true, identity_blind = true, equivalent_cases = true })
        };
        db.FairnessAudits.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return ToFairnessDto(record);
    }

    private static InterviewStageDto ToStageDto(Data.InterviewStageRecord x) =>
        new() { Id = x.Id, TenantId = x.TenantId, CandidateId = x.CandidateId, Stage = x.Stage, Status = x.Status, Score = x.Score, EvidenceJson = x.EvidenceJson, CreatedAt = x.CreatedAt };
    private static CandidateAppealDto ToAppealDto(Data.CandidateAppealRecord x) =>
        new() { Id = x.Id, TenantId = x.TenantId, CandidateId = x.CandidateId, Reason = x.Reason, Status = x.Status, Resolution = x.Resolution };
    private static FairnessAuditDto ToFairnessDto(Data.FairnessAuditRecord x) =>
        new() { Id = x.Id, TenantId = x.TenantId, PositionId = x.PositionId, Period = x.Period, SampleSize = x.SampleSize, SelectionRateRatio = x.SelectionRateRatio, Status = x.Status, DetailsJson = x.DetailsJson };

    private async Task<int> GetDepthAsync(PositionRecord position, Guid tenantId, CancellationToken cancellationToken)
    {
        var depth = 1;
        var current = position;
        while (current.ParentPositionId is { } parentId)
        {
            current = await db.Positions.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == parentId, cancellationToken);
            depth++;
        }
        return depth;
    }

    private static CharterDto ToCharter(CharterRecord x) => new(x.Mission,
        JsonSerializer.Deserialize<string[]>(x.ConstraintsJson) ?? [],
        x.MonthlyBudgetMinor, x.Currency,
        JsonSerializer.Deserialize<Dictionary<string, string>>(x.GovernanceJson) ?? new(), x.Version);
    private static PositionDto ToPosition(PositionRecord x) => new(x.Id, x.Name, x.Responsibilities,
        JsonSerializer.Deserialize<string[]>(x.CapabilitiesJson) ?? [], x.BudgetMinor, x.ParentPositionId, x.Status, x.Version);
    private static EmploymentPeriodDto ToPeriod(EmploymentPeriodRecord x) => new(x.Id, x.EmployeeId, x.PositionId, x.Kind, x.StartedAt, x.EndedAt);
    private static OrgDelegationDto ToDelegation(OrgDelegationRecord x) => new(x.Id, x.DelegateEmployeeId, x.SubtreePositionId,
        JsonSerializer.Deserialize<string[]>(x.ActionsJson) ?? [], x.BudgetLimitMinor, x.ExpiresAt, x.Status);
    private static CandidateDto ToCandidate(CandidateRecord x) => new(x.Id, x.PositionId, x.DisplayName, x.ModelVersion, x.Status, x.AssessmentJson, x.FairnessJson);
}
