using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using RegentAgent.Agents;
using RegentAgent.Contracts.Runtime;
using RegentAgent.Sandbox;

namespace RegentAgent.Runner;

public sealed class Worker(
    IHttpClientFactory clients,
    IAgentExecutor agent,
    ISandboxExecutor sandbox,
    IConfiguration configuration,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly string runnerId = configuration["Runner:Id"] ?? $"runner-{Environment.MachineName}";
    private readonly string tenantId = configuration["Runner:TenantId"] ?? "11111111-1111-1111-1111-111111111111";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var control = clients.CreateClient("controlplane");
        await control.PostAsJsonAsync("/internal/runners/register",
            new RunnerRegistration(runnerId, ["local", "documentation", "research"]), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await control.PostAsJsonAsync("/internal/runners/heartbeat",
                    new RunnerHeartbeat(runnerId, "Active", 0), stoppingToken);
                using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/tasks/claim");
                request.Headers.Add("X-Runner-Id", runnerId);
                request.Headers.Add("X-Regent-Tenant", tenantId);
                if (!string.IsNullOrWhiteSpace(configuration["Runner:Token"]))
                    request.Headers.Add("X-Regent-Runner-Token", configuration["Runner:Token"]);
                using var response = await control.SendAsync(request, stoppingToken);
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                var task = await response.Content.ReadFromJsonAsync<TaskClaim>(stoppingToken);
                if (task is null) continue;

                var result = await agent.ExecuteAsync(task, stoppingToken);
                var sandboxResult = await sandbox.WriteAsync(new SandboxRequest(
                    Guid.Parse(tenantId), task.TaskId, "result.md", result.Output), stoppingToken);
                var evidence = result.EvidenceRefs.Append($"sandbox:{sandboxResult.Sha256}").ToArray();
                await control.PostAsJsonAsync($"/internal/tasks/{task.TaskId}/complete",
                    new TaskCompletion(task.LeaseToken, result.Output, result.Status, evidence), stoppingToken);
                logger.LogInformation("Completed task {TaskId} with local Runner.", task.TaskId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Runner iteration failed; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
