using Microsoft.EntityFrameworkCore;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class ApprovalExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<ApprovalExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
                var rls = scope.ServiceProvider.GetRequiredService<TenantRlsScope>();
                var expiry = scope.ServiceProvider.GetRequiredService<ApprovalExpiryService>();
                foreach (var tenantId in await db.Tenants.AsNoTracking().Select(x => x.Id).ToListAsync(stoppingToken))
                    await rls.ExecuteAsync(tenantId, null, token => expiry.ExpireDueAsync(token), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Approval expiry worker failed."); }
        }
    }
}
