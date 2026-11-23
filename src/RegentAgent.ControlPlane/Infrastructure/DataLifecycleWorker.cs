using RegentAgent.ControlPlane.Application;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class DataLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<DataLifecycleWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<DataLifecycleService>().RunDuePurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Data lifecycle worker failed."); }
        }
    }
}
