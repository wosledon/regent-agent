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
        using (var register = CreateControlRequest(HttpMethod.Post, "/internal/runners/register"))
        {
            register.Content = JsonContent.Create(new RunnerRegistration(runnerId, ["local", "documentation", "research"]));
            using var response = await control.SendAsync(register, stoppingToken);
            response.EnsureSuccessStatusCode();
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var heartbeat = CreateControlRequest(HttpMethod.Post, "/internal/runners/heartbeat"))
                {
                    heartbeat.Content = JsonContent.Create(new RunnerHeartbeat(runnerId, "Active", 0));
                    using var heartbeatResponse = await control.SendAsync(heartbeat, stoppingToken);
                    heartbeatResponse.EnsureSuccessStatusCode();
                }
                using var request = CreateControlRequest(HttpMethod.Post, "/internal/tasks/claim");
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
                using var completeRequest = new HttpRequestMessage(HttpMethod.Post, $"/agent/v1/tasks/{task.TaskId}/complete");
                completeRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", task.AssignmentToken);
                completeRequest.Headers.Add("Idempotency-Key", $"task:{task.TaskId}:complete");
                completeRequest.Content = JsonContent.Create(new TaskCompletion(task.LeaseToken, result.Output, result.Status, evidence));
                using var completeResponse = await control.SendAsync(completeRequest, stoppingToken);
                completeResponse.EnsureSuccessStatusCode();
                logger.LogInformation("Completed task {TaskId} as employee Agent {EmployeeId}.", task.TaskId, task.EmployeeId);
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

    private HttpRequestMessage CreateControlRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Runner-Id", runnerId);
        request.Headers.Add("X-Regent-Tenant", tenantId);
        if (!string.IsNullOrWhiteSpace(configuration["Runner:Token"]))
            request.Headers.Add("X-Regent-Runner-Token", configuration["Runner:Token"]);
        return request;
    }
}
