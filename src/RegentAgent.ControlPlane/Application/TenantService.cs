using Microsoft.EntityFrameworkCore;
using RegentAgent.Contracts.Organization;
using RegentAgent.ControlPlane.Data;
using RegentAgent.ControlPlane.Identity;
using RegentAgent.ControlPlane.Infrastructure;

namespace RegentAgent.ControlPlane.Application;

public sealed class TenantService(RegentDbContext db, ITenantContext tenant, TimeProvider clock, TenantRlsScope rlsScope)
{
    public async Task<TenantDto> CreateAsync(string name, string companyName, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await rlsScope.ExecuteAsync(id, tenant.UserId.Value, async token =>
        {
            db.Tenants.Add(new TenantRecord { Id = id, Name = name, CompanyName = companyName });
            db.Memberships.Add(new MembershipRecord
            {
                Id = Guid.NewGuid(), TenantId = id, UserId = tenant.UserId.Value, Role = "Owner"
            });
            db.Charters.Add(new CharterRecord
            {
                TenantId = id, Mission = "Build useful software products autonomously.",
                MonthlyBudgetMinor = 8_000_000, Currency = "CNY", Version = 1, UpdatedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return new TenantDto(id, name, companyName, "Active");
    }

    public async Task<IReadOnlyList<TenantDto>> ListForUserAsync(CancellationToken cancellationToken)
    {
        var userId = tenant.UserId.Value;
        var tenants = await rlsScope.ExecuteAsync(null, userId, token => db.Memberships.AsNoTracking().Where(x => x.UserId == userId)
            .Join(db.Tenants.AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => t)
            .ToListAsync(token), cancellationToken);
        return tenants.Select(x => new TenantDto(x.Id, x.Name, x.CompanyName, x.Status)).ToArray();
    }
}
