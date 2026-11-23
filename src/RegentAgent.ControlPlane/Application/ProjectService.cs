using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Primitives;
using RegentAgent.Contracts.Projects;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;

namespace RegentAgent.ControlPlane.Application;

public sealed class ProjectService(RegentDbContext db, ITenantContext tenant, TimeProvider clock)
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
        var record = new ProjectRecord
        {
            Id = Guid.NewGuid(), TenantId = tenant.TenantId.Value, Name = input.Name, Mission = input.Mission,
            Owner = input.Owner, Stage = ProjectStage.BriefReady, Health = ProjectHealth.PendingAcceptance,
            TargetAt = clock.GetUtcNow().AddDays(input.TargetDays), BudgetMinor = input.BudgetMinor,
            Currency = input.Currency, UpdatedAt = clock.GetUtcNow()
        };
        db.Projects.Add(record);
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
