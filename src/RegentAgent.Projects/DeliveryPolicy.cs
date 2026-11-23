using RegentAgent.Contracts.Projects;

namespace RegentAgent.Projects;

public static class DeliveryPolicy
{
    public static IReadOnlyList<string> ValidateFinding(ResearchFinding finding)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(finding.Claim)) errors.Add("Claim is required.");
        if (finding.KeyClaim && finding.Sources.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            errors.Add("Key claims require two independent sources.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateRequirementReady(DeliveryRequirement requirement)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(requirement.Scope)) errors.Add("Scope is required.");
        if (requirement.AcceptanceCriteria.Count == 0 || requirement.AcceptanceCriteria.Any(string.IsNullOrWhiteSpace))
            errors.Add("Acceptance criteria are required.");
        if (requirement.Priority is not ("P0" or "P1" or "P2")) errors.Add("Priority is invalid.");
        return errors;
    }

    public static IReadOnlyList<string> ValidatePlan(DeliveryRequirement requirement, DecompositionPlan plan)
    {
        var errors = new List<string>();
        if (plan.RequirementVersion != requirement.Version) errors.Add("Requirement version changed; create a revised plan.");
        if (plan.Tasks.Count is < 3 or > 12) errors.Add("A plan requires 3 to 12 tasks.");
        if (plan.Tasks.Sum(x => x.Weight) > requirement.TotalWeight) errors.Add("Child task weight exceeds requirement weight.");
        if (plan.Tasks.Select(x => x.Id).Distinct().Count() != plan.Tasks.Count) errors.Add("Task identifiers must be unique.");
        if (plan.Tasks.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Weight <= 0)) errors.Add("Every task needs a title and positive weight.");
        foreach (var dependency in plan.Tasks.SelectMany(x => x.DependsOn))
        {
            if (plan.Tasks.All(x => x.Id != dependency)) errors.Add("Dependency is outside the plan.");
        }
        if (HasCycle(plan.Tasks)) errors.Add("Task dependency graph contains a cycle.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateTaskDone(DeliveryTask task, DoneEvidence evidence,
        IReadOnlyList<Review> reviews, IReadOnlyList<Defect> defects)
    {
        var errors = new List<string>();
        if (evidence.WorkItemId != task.Id) errors.Add("Evidence belongs to another work item.");
        if (evidence.Validator == task.Assignee || string.IsNullOrWhiteSpace(evidence.Validator))
            errors.Add("An independent validator is required.");
        if (evidence.CriteriaResults.Any(x => !x.Value)) errors.Add("One or more completion criteria failed.");
        if (evidence.Artifacts.Count == 0) errors.Add("At least one artifact is required.");
        if (evidence.TestResults.Count == 0) errors.Add("At least one test result is required.");
        if (evidence.SecurityChecks.Count == 0) errors.Add("Security checks are required.");
        if (reviews.All(x => x.SubjectId != task.Id || x.Verdict != "Approved"))
            errors.Add("Independent approval is required.");
        var blocking = defects.Count(x => (x.TaskId == task.Id || x.RequirementId == task.RequirementId) && x.Severity is "P0" or "P1" && x.Status != "Closed");
        if (blocking > 0) errors.Add("Open P0/P1 defects block completion.");
        if (evidence.OpenP0P1Defects != blocking) errors.Add("Evidence defect count is stale.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateRelease(ProjectDeliveryState state, Guid projectId, string artifactRef)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(artifactRef)) errors.Add("Release artifact is required.");
        var requirements = state.Requirements.Where(x => x.ProjectId == projectId).ToArray();
        if (requirements.Length == 0) errors.Add("At least one requirement is required.");
        if (requirements.Any(x => x.Status != "Accepted")) errors.Add("All requirements must be accepted.");
        var requirementIds = requirements.Select(x => x.Id).ToHashSet();
        foreach (var requirementId in requirementIds)
        {
            var reports = state.AcceptanceReports.Where(x => x.RequirementId == requirementId).ToArray();
            if (reports.Length is < 2 or > 3 || reports.Any(x => !x.Passed))
                errors.Add("Each requirement needs two to three passing independent acceptance reports.");
        }
        if (state.Defects.Any(x => x.ProjectId == projectId && x.Severity is "P0" or "P1" && x.Status != "Closed"))
            errors.Add("Open P0/P1 defects block release.");
        return errors;
    }

    private static bool HasCycle(IReadOnlyList<DeliveryTask> tasks)
    {
        var map = tasks.ToDictionary(x => x.Id, x => x.DependsOn);
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (visiting.Contains(id)) return true;
            if (visited.Contains(id)) return false;
            visiting.Add(id);
            if (!map.TryGetValue(id, out var dependencies)) return false;
            foreach (var dependency in dependencies) if (Visit(dependency)) return true;
            visiting.Remove(id);
            visited.Add(id);
            return false;
        }
        return tasks.Any(x => Visit(x.Id));
    }
}
