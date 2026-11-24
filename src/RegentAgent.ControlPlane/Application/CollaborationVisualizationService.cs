using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RegentAgent.Chat;
using RegentAgent.Contracts.Collaboration;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public sealed class CollaborationVisualizationService(RegentDbContext db, ChatService chat, TimeProvider clock)
{
    public async Task<CollaborationGraphDto> GetGraphAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.EmployeeNo).ToListAsync(cancellationToken);
        var positions = await db.Positions.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var periods = await db.EmploymentPeriods.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EndedAt == null).ToListAsync(cancellationToken);
        var projects = await db.Projects.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var tasks = await db.Tasks.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var work = await db.WorkEntries.AsNoTracking().Where(x => x.TenantId == tenantId).ToListAsync(cancellationToken);
        var conversations = await chat.ListAsync(tenantId.ToString(), cancellationToken);
        var messages = await chat.ListAllMessagesAsync(tenantId.ToString(), cancellationToken);
        var audits = await db.AuditRecords.AsNoTracking().Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Seq).Take(500).ToListAsync(cancellationToken);

        var employeeById = employees.ToDictionary(x => x.Id, x => x);
        var employeeByName = employees.ToDictionary(x => x.DisplayName, x => x, StringComparer.OrdinalIgnoreCase);
        var positionById = positions.ToDictionary(x => x.Id, x => x);
        var employeePosition = periods.Where(x => employeeById.ContainsKey(x.EmployeeId) && positionById.ContainsKey(x.PositionId))
            .ToDictionary(x => x.EmployeeId, x => positionById[x.PositionId]);
        var positionEmployee = employeePosition.ToDictionary(x => x.Value.Id, x => x.Key);
        var tasksByProject = tasks.GroupBy(x => x.ProjectId).ToDictionary(x => x.Key, x => x.ToArray());
        var projectById = projects.ToDictionary(x => x.Id, x => x);

        var nodes = new List<CollaborationNodeDto>();
        for (var index = 0; index < employees.Count; index++)
        {
            var employee = employees[index];
            var position = employeePosition.GetValueOrDefault(employee.Id);
            var depth = PositionDepth(position, positionById);
            var employeeTasks = tasks.Where(x => string.Equals(x.Assignee, employee.DisplayName, StringComparison.OrdinalIgnoreCase)).ToArray();
            var currentTask = employeeTasks.FirstOrDefault(x => x.Status is "InProgress" or "InReview" or "Blocked") ?? employeeTasks.FirstOrDefault();
            var managerId = position?.ParentPositionId is { } parentPositionId
                ? positionEmployee.GetValueOrDefault(parentPositionId)
                : (Guid?)null;
            nodes.Add(new CollaborationNodeDto($"employee:{employee.Id}", "Employee", employee.DisplayName,
                $"{employee.EmployeeNo} · {position?.Name ?? employee.Position}", employee.State.ToString(),
                employee.Id.ToString(), null, currentTask?.ProjectId.ToString(), position?.Id.ToString(), managerId?.ToString(),
                depth, 60 + (index % 4) * 230, 70 + (index / 4) * 150, [], 0));
        }
        for (var index = 0; index < conversations.Count; index++)
        {
            var conversation = conversations[index];
            var messageCount = messages.Count(x => x.ConversationId == conversation.Id);
            nodes.Add(new CollaborationNodeDto($"conversation:{conversation.Id}", "Conversation", conversation.Title,
                $"{conversation.Kind} · {conversation.MemberEmployeeIds.Count} members · {messageCount} messages", conversation.Status,
                null, conversation.Id, conversation.ScopeId, null, null, 0,
                1010 + (index % 2) * 270, 70 + (index / 2) * 150, conversation.MemberEmployeeIds, 0));
        }
        for (var index = 0; index < projects.Count; index++)
        {
            var project = projects[index];
            var projectTasks = tasksByProject.GetValueOrDefault(project.Id) ?? [];
            nodes.Add(new CollaborationNodeDto($"project:{project.Id}", "Project", project.Name,
                $"{project.Stage} · {projectTasks.Length} tasks · {project.Health}", project.Health.ToString(),
                null, null, project.Id.ToString(), null, null, 0,
                60 + (index % 3) * 270, 380 + (index / 3) * 150, [], 0));
        }

        var edges = new Dictionary<string, CollaborationEdgeDto>();
        foreach (var employee in employees)
        {
            if (!employeePosition.TryGetValue(employee.Id, out var position) || position.ParentPositionId is not { } parentId
                || !positionEmployee.TryGetValue(parentId, out var managerId)) continue;
            AddEdge(edges, "reports_to", $"employee:{employee.Id}", $"employee:{managerId}", 1, "Reports to");
        }
        foreach (var conversation in conversations)
        {
            foreach (var memberId in conversation.MemberEmployeeIds)
                if (Guid.TryParse(memberId, out var id) && employeeById.ContainsKey(id))
                    AddEdge(edges, "member_of", $"employee:{id}", $"conversation:{conversation.Id}", 1, "Member");
        }
        foreach (var task in tasks)
        {
            if (!projectById.ContainsKey(task.ProjectId) || !employeeByName.TryGetValue(task.Assignee, out var employee)) continue;
            AddEdge(edges, "works_on", $"employee:{employee.Id}", $"project:{task.ProjectId}", task.Weight, task.Title);
        }
        var collaborationWeights = new Dictionary<(string Source, string Target), int>();
        foreach (var message in messages)
        {
            if (!employeeByName.TryGetValue(message.Sender, out var sender)) continue;
            var conversation = conversations.FirstOrDefault(x => x.Id == message.ConversationId);
            if (conversation is null) continue;
            foreach (var memberId in conversation.MemberEmployeeIds)
            {
                if (!Guid.TryParse(memberId, out var member) || member == sender.Id || !employeeById.ContainsKey(member)) continue;
                var source = $"employee:{sender.Id}";
                var target = $"employee:{member}";
                if (string.CompareOrdinal(source, target) > 0) (source, target) = (target, source);
                collaborationWeights[(source, target)] = collaborationWeights.GetValueOrDefault((source, target)) + 1;
            }
        }
        foreach (var pair in collaborationWeights)
            AddEdge(edges, "collaborates", pair.Key.Source, pair.Key.Target, pair.Value, $"{pair.Value} shared messages");

        var activities = new List<CollaborationActivityDto>();
        foreach (var entry in work)
        {
            var employee = employeeById.GetValueOrDefault(entry.EmployeeId);
            var evidence = JsonSerializer.Deserialize<string[]>(entry.EvidenceJson) ?? [];
            activities.Add(new CollaborationActivityDto($"work:{entry.Id}", entry.OccurredAt, "Work", entry.Title,
                employee?.DisplayName ?? "Employee", "Agent", entry.TaskId is null ? "Work" : "Task",
                entry.TaskId?.ToString() ?? entry.Id.ToString(), entry.EmployeeId.ToString(), null,
                entry.TaskId is { } taskId ? tasks.FirstOrDefault(x => x.Id == taskId)?.ProjectId.ToString() : null,
                entry.TaskId?.ToString(), evidence, null));
        }
        foreach (var message in messages)
        {
            var sender = Guid.TryParse(message.SenderEmployeeId, out var senderId)
                ? employeeById.GetValueOrDefault(senderId)
                : employeeByName.GetValueOrDefault(message.Sender);
            var title = message.Body.Length <= 160 ? message.Body : message.Body[..160] + "…";
            activities.Add(new CollaborationActivityDto($"message:{message.Id}", message.CreatedAt, "Message",
                $"{message.MessageType}: {title}", message.Sender, message.ActorType, "Message", message.Id, sender?.Id.ToString(),
                message.ConversationId, null, null, message.EvidenceRefs, null));
        }
        foreach (var audit in audits)
        {
            var refs = ReadPayloadRefs(audit.PayloadJson);
            activities.Add(new CollaborationActivityDto($"audit:{audit.Seq}", audit.CreatedAt, "Audit", audit.EventType,
                audit.Actor, ReadPayloadActorType(audit.PayloadJson), "Audit", refs.SourceId ?? audit.Seq.ToString(), refs.EmployeeId, refs.ConversationId,
                refs.ProjectId, refs.TaskId, [], audit.Hash));
        }
        activities = activities.OrderByDescending(x => x.OccurredAt).Take(300).ToList();

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var count = activities.Count(x =>
                (node.EmployeeId is not null && x.EmployeeId == node.EmployeeId)
                || (node.ConversationId is not null && x.ConversationId == node.ConversationId)
                || (node.ProjectId is not null && x.ProjectId == node.ProjectId));
            nodes[index] = node with { ActivityCount = count };
        }
        var summary = new CollaborationSummaryDto(employees.Count, conversations.Count, messages.Count,
            edges.Count(x => x.Value.Kind == "collaborates"), projects.Count, activities.Count);
        return new CollaborationGraphDto(nodes, edges.Values.OrderBy(x => x.Kind).ThenBy(x => x.SourceId).ToArray(),
            activities, summary, clock.GetUtcNow());
    }

    public async Task<CollaborationTraceDto> GetTraceAsync(Guid tenantId, string entityType, string entityId,
        CancellationToken cancellationToken)
    {
        var graph = await GetGraphAsync(tenantId, cancellationToken);
        var normalized = entityType.Trim().ToLowerInvariant();
        var activities = graph.Activities.Where(x => normalized switch
        {
            "employee" => x.EmployeeId == entityId || x.SourceId == entityId,
            "conversation" => x.ConversationId == entityId || x.SourceId == entityId,
            "project" => x.ProjectId == entityId || x.SourceId == entityId,
            "task" => x.TaskId == entityId || x.SourceId == entityId,
            "message" or "audit" => x.SourceId == entityId,
            _ => false
        }).ToArray();
        return new CollaborationTraceDto(normalized, entityId, activities, clock.GetUtcNow());
    }

    private static void AddEdge(Dictionary<string, CollaborationEdgeDto> edges, string kind, string source, string target,
        int weight, string label)
    {
        var id = $"{kind}:{source}:{target}";
        if (edges.TryGetValue(id, out var existing)) edges[id] = existing with { Weight = existing.Weight + weight, Label = label };
        else edges[id] = new CollaborationEdgeDto(id, source, target, kind, weight, label);
    }

    private static int PositionDepth(PositionRecord? position, IReadOnlyDictionary<Guid, PositionRecord> positions)
    {
        var depth = 0;
        var current = position;
        var visited = new HashSet<Guid>();
        while (current?.ParentPositionId is { } parentId && positions.TryGetValue(parentId, out var parent) && visited.Add(parent.Id))
        {
            depth++;
            current = parent;
        }
        return depth;
    }

    private static (string? SourceId, string? EmployeeId, string? ConversationId, string? ProjectId, string? TaskId)
        ReadPayloadRefs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return (Read(root, "message_id", "conversation_id", "employee_id", "project_id", "task_id", "action_id", "id"),
                Read(root, "employee_id"), Read(root, "conversation_id"), Read(root, "project_id"), Read(root, "task_id"));
        }
        catch (JsonException) { return (null, null, null, null, null); }
    }

    private static string ReadPayloadActorType(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return Read(document.RootElement, "actor_type") ?? "System";
        }
        catch (JsonException) { return "System"; }
    }

    private static string? Read(JsonElement root, params string[] keys)
    {
        foreach (var key in keys)
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value)
                && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();
        return null;
    }
}
