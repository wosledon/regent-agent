using Microsoft.Extensions.DependencyInjection;

namespace RegentAgent.ControlPlane.Realtime;

public sealed class TenantHubSessionRevalidator(
    IServiceScopeFactory scopeFactory,
    TenantHubConnectionRegistry connections) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RevalidateOnceAsync(stoppingToken);
    }

    internal async Task RevalidateOnceAsync(CancellationToken cancellationToken)
    {
        foreach (var (connectionId, connection) in connections.Snapshot())
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var validator = scope.ServiceProvider.GetRequiredService<ITenantSessionValidator>();
                if (await validator.ValidateAsync(connection.SessionId, connection.UserId, connection.TenantId,
                        connection.EmployeeId, connection.CredentialEpoch, cancellationToken)) continue;
                await connections.DisconnectConnectionAsync(connectionId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // A failed revalidation must not leave a connection authorized indefinitely.
                try
                {
                    await connections.DisconnectConnectionAsync(connectionId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch { /* Keep the registration so the next revalidation can retry. */ }
            }
        }
    }
}
