using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.Contracts.Agents;
using RegentAgent.Contracts.Governance;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application.AgentTools;

public sealed class AgentToolInvocationService(
    IEnumerable<IAgentToolHandler> handlers,
    AgentToolCatalog catalog,
    GovernanceService governance,
    AuditIntegrityService audit,
    IdempotencyService idempotency,
    RegentDbContext db)
{
    private readonly IAgentToolHandler[] handlers = handlers.ToArray();
    private readonly AgentToolCatalog catalog = catalog;
    private readonly GovernanceService governance = governance;
    private readonly AuditIntegrityService audit = audit;
    private readonly IdempotencyService idempotency = idempotency;
    private readonly RegentDbContext db = db;

    public Task<IReadOnlyList<ToolManifest>> DiscoverAsync(AgentIdentityModel identity,
        CancellationToken cancellationToken) => catalog.DiscoverAsync(identity.TenantId, identity.Scopes, cancellationToken);

    public async Task<AgentToolInvocationResult> InvokeAsync(AgentIdentityModel identity,
        AgentToolInvocationRequest request, CancellationToken cancellationToken)
    {
        if (identity.TaskId is null) throw new UnauthorizedAccessException("A task-scoped Agent token is required.");
        await catalog.EnsureRegisteredAsync(identity.TenantId, cancellationToken);
        var handler = handlers.FirstOrDefault(candidate => candidate.Version == request.Version
            && request.ToolName.StartsWith(candidate.ToolName + ".", StringComparison.Ordinal));
        if (handler is null || !handler.Capabilities.Contains(request.Capability, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Tool handler is not available.");
        if (!identity.Scopes.Contains(request.Capability, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Agent token does not include the required capability.");

        var operation = $"agent-tool:{identity.EmployeeId}:{request.ToolName}:{request.Version}";
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var repeated = await idempotency.GetCompletedAsync<AgentToolInvocationResult>(identity.TenantId,
                request.IdempotencyKey, operation, cancellationToken);
            if (repeated is not null) return repeated;
        }

        var trustedGrant = System.Text.Json.JsonSerializer.Serialize(new { capabilities = identity.Scopes });
        var effectiveTaint = GovernancePolicy.ScanInjection(request.ArgumentsJson).Count > 0 ? "High" : request.Taint;
        var authorizationRequest = new ToolAuthorizationRequest(request.ToolName, request.Version,
            identity.EmployeeId.ToString(), request.Capability, trustedGrant, effectiveTaint, request.ArgumentsJson);
        var authorization = await governance.AuthorizeToolCallAsync(identity.TenantId, authorizationRequest,
            cancellationToken);
        await governance.RecordToolUsageAsync(identity.TenantId, authorizationRequest, authorization, cancellationToken);
        if (!authorization.Allowed)
        {
            var deniedAudit = await audit.AppendAsync(identity.TenantId, "agent.tool.denied",
                identity.EmployeeId.ToString(), new
                {
                    actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId,
                    tool = request.ToolName, capability = request.Capability, reason = authorization.Reason
                }, cancellationToken);
            return new AgentToolInvocationResult(false, false, authorization.Reason, authorization.Digest, null,
                deniedAudit.Hash);
        }

        JsonElement arguments;
        try { arguments = JsonSerializer.Deserialize<JsonElement>(request.ArgumentsJson); }
        catch (JsonException) { throw new InvalidOperationException("Tool arguments must be valid JSON."); }
        var employee = await db.Employees.AsNoTracking().SingleAsync(
            x => x.TenantId == identity.TenantId && x.Id == identity.EmployeeId, cancellationToken);
        var context = new AgentToolContext(identity, employee.DisplayName, trustedGrant, effectiveTaint,
            request.ArgumentsJson);
        object result;
        try
        {
            result = await handler.ExecuteAsync(context, arguments, cancellationToken);
        }
        catch
        {
            await audit.AppendAsync(identity.TenantId, "agent.tool.failed", employee.DisplayName,
                new
                {
                    actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId,
                    tool = request.ToolName, capability = request.Capability
                }, cancellationToken);
            throw;
        }
        var invokedAudit = await audit.AppendAsync(identity.TenantId, "agent.tool.invoked", employee.DisplayName,
            new
            {
                actor_type = "Agent", employee_id = identity.EmployeeId, task_id = identity.TaskId,
                tool = request.ToolName, capability = request.Capability, digest = authorization.Digest
            }, cancellationToken);
        var response = new AgentToolInvocationResult(true, true, null, authorization.Digest, result, invokedAudit.Hash);
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            await idempotency.SaveCompletedAsync(identity.TenantId, request.IdempotencyKey, operation, response,
                cancellationToken);
        return response;
    }
}
