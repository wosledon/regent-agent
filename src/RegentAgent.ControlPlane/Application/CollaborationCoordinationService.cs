using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using AgentIdentityModel = RegentAgent.AgentIdentity.AgentIdentity;
using RegentAgent.AgentIdentity;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Pagination;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed partial class CollaborationCoordinationService(
    RegentDbContext db,
    TimeProvider clock,
    AuditIntegrityService audit,
    IdempotencyService idempotency,
    CollaborationOptions options)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly string[] ActiveTaskStatuses = ["Pending", "Assigned", "InProgress", "Blocked"];
    private static readonly string[] WipStatuses = ["Assigned", "InProgress", "Blocked"];

    public async Task<Page<TaskCoordinationDto>> ListAsync(Guid tenantId, CoordinationPageQuery query,
        CancellationToken cancellationToken)
    {
        var limit = query.NormalizedLimit;
        var items = db.TaskCoordinations.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Kind)) items = items.Where(x => x.Kind == query.Kind);
        if (!string.IsNullOrWhiteSpace(query.Status)) items = items.Where(x => x.Status == query.Status);
        if (query.EmployeeId is { } employeeId)
            items = query.IncomingOnly == true
                ? items.Where(x => x.TargetEmployeeId == employeeId)
                : items.Where(x => x.RequesterEmployeeId == employeeId || x.TargetEmployeeId == employeeId);
        if (TryReadCursor(query.Cursor, out var cursorSortKey)) items = items.Where(x => x.SortKey.CompareTo(cursorSortKey) < 0);
        var records = await items.OrderByDescending(x => x.SortKey).Take(limit + 1).ToListAsync(cancellationToken);
        var nextCursor = records.Count > limit ? EncodeCursor(records[limit - 1].SortKey) : null;
        return new Page<TaskCoordinationDto>(records.Take(limit).Select(ToDto).ToArray(), nextCursor, clock.GetUtcNow());
    }

    public async Task<TaskCoordinationDto?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
    {
        var record = await db.TaskCoordinations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        return record is null ? null : ToDto(record);
    }

    private async Task<T> WriteAsync<T>(Guid tenantId, Guid actorEmployeeId, string operation,
        string? idempotencyKey, Func<Task<T>> write, CancellationToken cancellationToken)
    {
        operation = $"{operation}:{actorEmployeeId}";
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var repeated = await idempotency.GetCompletedAsync<T>(tenantId, idempotencyKey, operation, cancellationToken);
            if (repeated is not null) return repeated;
        }
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var result = await write();
        if (result is null) throw new InvalidOperationException("Coordination write returned no result.");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            await idempotency.SaveCompletedAsync(tenantId, idempotencyKey, operation, result, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<TaskRecord> LoadOwnedTaskAsync(AgentIdentityModel identity, Guid taskId,
        bool requireLease, CancellationToken cancellationToken)
    {
        RequireTaskScope(identity);
        var task = await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == identity.TenantId && x.Id == taskId,
            cancellationToken) ?? throw new KeyNotFoundException("Task not found.");
        if (identity.TaskId != taskId) throw new UnauthorizedAccessException("Agent token is not scoped to this task.");
        var owner = await ResolveOwnerAsync(task, cancellationToken);
        if (owner != identity.EmployeeId) throw new UnauthorizedAccessException("Agent does not own this task.");
        if (!ActiveTaskStatuses.Contains(task.Status)) throw new InvalidOperationException("Task is not active.");
        if (requireLease && (string.IsNullOrWhiteSpace(task.LeaseToken) || task.LeaseExpiresAt <= clock.GetUtcNow()))
            throw new InvalidOperationException("An active task lease is required.");
        return task;
    }

    private async Task<EmployeeRecord> LoadTargetAsync(Guid tenantId, Guid employeeId,
        CancellationToken cancellationToken)
    {
        var employee = await db.Employees.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId,
            cancellationToken) ?? throw new KeyNotFoundException("Target employee not found.");
        if (employee.State is not (EmployeeState.Active or EmployeeState.Busy))
            throw new InvalidOperationException("Target employee is not available.");
        return employee;
    }

    private async Task<TaskRecord> LoadTaskAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.Tasks.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == taskId, cancellationToken)
        ?? throw new KeyNotFoundException("Task not found.");

    private async Task<TaskCoordinationRecord> LoadCoordinationAsync(Guid tenantId, Guid id,
        CancellationToken cancellationToken) =>
        await db.TaskCoordinations.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken)
        ?? throw new KeyNotFoundException("Coordination request not found.");

    private static void RequireTaskScope(AgentIdentityModel identity)
    {
        if (identity.TaskId is null || !identity.Scopes.Contains("tasks:execute", StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("A task-scoped Agent token with tasks:execute is required.");
    }

    private static void RequireParticipant(TaskCoordinationRecord record, Guid employeeId, bool targetOnly = false)
    {
        if (targetOnly ? record.TargetEmployeeId != employeeId
            : record.RequesterEmployeeId != employeeId && record.TargetEmployeeId != employeeId)
            throw new UnauthorizedAccessException("Agent is not a participant in this coordination request.");
    }

    private async Task<Guid> ResolveOwnerAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        if (task.AssigneeEmployeeId is { } assigneeId) return assigneeId;
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == task.TenantId && x.DisplayName == task.Assignee, cancellationToken);
        return employee?.Id ?? Guid.Empty;
    }

    private async Task EnsureDelegateEligibleAsync(Guid tenantId, Guid employeeId, Guid requesterId,
        IReadOnlyList<string> requiredCapabilities, Guid excludedTaskId, CancellationToken cancellationToken)
    {
        var target = await LoadTargetAsync(tenantId, employeeId, cancellationToken);
        if (target.Id == requesterId) throw new InvalidOperationException("Delegation target must differ from the requester.");
        var capabilities = new HashSet<string>(AgentScopes.Baseline, StringComparer.OrdinalIgnoreCase);
        var period = await db.EmploymentPeriods.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.EndedAt == null, cancellationToken);
        if (period is not null)
        {
            var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == period.PositionId, cancellationToken);
            if (position is not null)
                foreach (var capability in JsonSerializer.Deserialize<string[]>(position.CapabilitiesJson) ?? [])
                    capabilities.Add(capability);
        }
        if (requiredCapabilities.Any(capability => !capabilities.Contains(capability)))
            throw new InvalidOperationException("Target employee lacks the required capabilities.");
        var currentWip = await db.Tasks.AsNoTracking().CountAsync(x => x.TenantId == tenantId && x.Id != excludedTaskId
            && (x.AssigneeEmployeeId == employeeId || x.Assignee == target.DisplayName)
            && WipStatuses.Contains(x.Status), cancellationToken);
        if (currentWip >= options.DefaultWipLimit)
            throw new InvalidOperationException("Target employee has reached the WIP limit.");
    }

    private async Task<TaskCoordinationRecord> CreateAsync(Guid tenantId, TaskRecord task,
        Guid requesterId, Guid targetId, string kind, string status, string priority, string summary,
        object details, string actorName, DateTimeOffset? deadline, string? leaseToken,
        CancellationToken cancellationToken)
    {
        ValidatePayload(JsonSerializer.Serialize(details, JsonOptions));
        var now = clock.GetUtcNow();
        var record = new TaskCoordinationRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, TaskId = task.Id, ProjectId = task.ProjectId,
            Kind = kind, Status = status, RequesterEmployeeId = requesterId, TargetEmployeeId = targetId,
            Priority = priority, Summary = summary, DetailsJson = JsonSerializer.Serialize(details, JsonOptions),
            SortKey = $"{now.UtcTicks:D20}:{Guid.NewGuid():N}", CreatedAt = now, UpdatedAt = now,
            Deadline = deadline, LeaseToken = leaseToken
        };
        db.TaskCoordinations.Add(record);
        await AppendAuditAsync(record, "created", requesterId, actorName, new { }, cancellationToken);
        await SaveAsync(cancellationToken);
        return record;
    }

    private async Task AppendAuditAsync(TaskCoordinationRecord record, string action, Guid actorEmployeeId,
        string actorName, object extra, CancellationToken cancellationToken)
    {
        await audit.AppendAsync(record.TenantId, $"collaboration.{record.Kind.ToLowerInvariant()}.{action}",
            actorName, new
            {
                actor_type = "Agent", employee_id = actorEmployeeId,
                target_employee_id = record.TargetEmployeeId, task_id = record.TaskId,
                project_id = record.ProjectId, coordination_id = record.Id, status = record.Status,
                priority = record.Priority, extra
            }, cancellationToken);
    }

    private void Touch(TaskRecord task)
    {
        task.Version++;
        task.UpdatedAt = clock.GetUtcNow();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("Coordination state changed concurrently."); }
    }

    private void ValidatePayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > options.MaxCoordinationPayloadChars)
            throw new InvalidOperationException("Coordination payload is empty or too large.");
        try { using var document = JsonDocument.Parse(json); }
        catch (JsonException) { throw new InvalidOperationException("Coordination payload must be valid JSON."); }
    }

    private static TaskCoordinationDto ToDto(TaskCoordinationRecord record) => new(
        record.Id, record.TenantId, record.TaskId, record.ProjectId, record.Kind, record.Status,
        record.RequesterEmployeeId, record.TargetEmployeeId, record.Priority, record.Summary,
        record.DetailsJson, record.ResponseJson, record.CreatedAt, record.UpdatedAt, record.Deadline,
        record.CompletedAt, record.Version);

    private static string EncodeCursor(string sortKey) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(sortKey));

    private static bool TryReadCursor(string? cursor, out string sortKey)
    {
        sortKey = string.Empty;
        if (string.IsNullOrWhiteSpace(cursor)) return false;
        try { sortKey = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)); return sortKey.Length > 0; }
        catch (FormatException) { throw new InvalidOperationException("Pagination cursor is invalid."); }
    }
}
