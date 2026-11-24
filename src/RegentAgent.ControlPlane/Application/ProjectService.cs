using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Billing;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class ProjectService(RegentDbContext db, ITenantContext tenant, TimeProvider clock, ITenantQuotaGuard quotaGuard)
{
    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var projects = (await db.Projects.AsNoTracking().Include(x => x.Milestones).Include(x => x.Tasks)
            .Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken)).OrderBy(x => x.TargetAt).ToList();
        var now = clock.GetUtcNow();
        return projects.Select(x => Mappings.ToSummary(x, FreshnessPolicy.Evaluate(x.UpdatedAt, now, TimeSpan.FromMinutes(10))))
            .ToArray();
    }

    public async Task<ProjectDetail> CreateAsync(CreateProjectDto input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Owner) || input.TargetDays <= 0 || input.BudgetMinor < 0)
            throw new InvalidOperationException("Project name, owner, target and budget are required.");
        await quotaGuard.EnsureAsync(tenant.TenantId.Value, "projects", 1, cancellationToken);
        var record = new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId.Value, Name = input.Name, Mission = input.Mission,
            Owner = input.Owner, Stage = ProjectStage.Proposed, Health = ProjectHealth.PendingAcceptance,
            TargetAt = clock.GetUtcNow().AddDays(input.TargetDays), BudgetMinor = input.BudgetMinor,
            Currency = input.Currency, UpdatedAt = clock.GetUtcNow()
        };
        db.Projects.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return Mappings.ToDetail(record, Freshness.Live);
    }

    public async Task<ProjectDetail?> TransitionAsync(ProjectId id, ProjectTransitionDto transition,
        CancellationToken cancellationToken)
    {
        var record = await db.Projects.SingleOrDefaultAsync(x => x.TenantId == tenant.TenantId.Value && x.Id == id.Value, cancellationToken);
        if (record is null) return null;
        var allowed = record.Stage switch
        {
            ProjectStage.Proposed => [ProjectStage.Approved, ProjectStage.Cancelled],
            ProjectStage.Approved => [ProjectStage.Active, ProjectStage.Cancelled],
            ProjectStage.Active => [ProjectStage.Paused, ProjectStage.Completed, ProjectStage.Cancelled],
            ProjectStage.Paused => [ProjectStage.Active, ProjectStage.Cancelled],
            ProjectStage.BriefReady => [ProjectStage.Approved, ProjectStage.Cancelled],
            ProjectStage.Building or ProjectStage.Verifying or ProjectStage.Releasing => [ProjectStage.Live, ProjectStage.Cancelled],
            ProjectStage.Live or ProjectStage.Iterating => [ProjectStage.Completed, ProjectStage.Cancelled],
            _ => Array.Empty<ProjectStage>()
        };
        if (!allowed.Contains(transition.Stage)) throw new InvalidOperationException("Project state transition is not allowed.");
        if (transition.Stage is ProjectStage.Completed or ProjectStage.Cancelled && string.IsNullOrWhiteSpace(transition.Evidence))
            throw new InvalidOperationException("Completion or cancellation requires evidence.");
        record.Stage = transition.Stage;
        record.Health = transition.Stage switch
        {
            ProjectStage.Completed or ProjectStage.Live => ProjectHealth.Live,
            ProjectStage.Cancelled => ProjectHealth.Failed,
            ProjectStage.Paused => ProjectHealth.AtRisk,
            _ => ProjectHealth.OnTrack
        };
        record.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Mappings.ToDetail(record, Freshness.Live);
    }

    public Task<bool> ExistsAsync(ProjectId id, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().AnyAsync(x => x.TenantId == tenant.TenantId.Value && x.Id == id.Value, cancellationToken);

    public async Task<ProjectDetail?> GetAsync(ProjectId id, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId.Value;
        var project = await db.Projects.AsNoTracking().Include(x => x.Milestones).Include(x => x.Tasks)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id.Value, cancellationToken);
        return project is null ? null : Mappings.ToDetail(project,
            FreshnessPolicy.Evaluate(project.UpdatedAt, clock.GetUtcNow(), TimeSpan.FromMinutes(10)));
    }
}
