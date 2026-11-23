using RegentAgent.ControlPlane.Application;
using RegentAgent.Evaluation;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class EvaluationRegressionWorker(IServiceScopeFactory scopeFactory, ILogger<EvaluationRegressionWorker> logger)
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
                await scope.ServiceProvider.GetRequiredService<EvaluationQaService>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Evaluation regression worker failed."); }
        }
    }
}
