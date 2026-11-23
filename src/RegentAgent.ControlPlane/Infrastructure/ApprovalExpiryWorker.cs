using RegentAgent.ControlPlane.Application;

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
                await scope.ServiceProvider.GetRequiredService<ApprovalExpiryService>().ExpireDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Approval expiry worker failed."); }
        }
    }
}
