using System.Net.Http.Json;
using System.Text.Json;
using RegentAgent.Contracts.Runtime;

namespace RegentAgent.Agents;

public sealed class LocalAgentExecutor(HttpClient gateway) : IAgentExecutor
{
    public async Task<AgentResult> ExecuteAsync(TaskClaim task, CancellationToken cancellationToken)
    {
        var prompt = $"""
            You are completing a local deterministic task.
            Task type: {task.TaskType}
            Title: {task.Title}
            Produce a concise markdown result with Summary, Actions, and Evidence sections.
            """;
        using var response = await gateway.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "local-cheap",
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 512
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var text = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? "Local task completed.";
        var output = $"# {task.Title}\n\n## Summary\n\n{text}\n\n## Actions\n\n- Executed by local Runner without containers.\n\n## Evidence\n\n- Task: {task.TaskId}\n- Type: {task.TaskType}\n";
        return new AgentResult(output, "Done", [$"task:{task.TaskId}", "provider:local"]);
    }
}
