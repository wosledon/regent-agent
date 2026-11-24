namespace RegentAgent.Gateway.Metering;

public sealed class BudgetExpiryWorker(LocalBudgetService budget, ILogger<BudgetExpiryWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var expired = await budget.ExpireAsync(stoppingToken);
                if (expired > 0) logger.LogInformation("Expired {Count} budget reservations.", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Budget reservation expiry failed.");
            }
        }
    }
}
