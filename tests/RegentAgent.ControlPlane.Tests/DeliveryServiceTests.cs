using System.Text.Json;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class DeliveryServiceTests
{
    [Fact]
    public async Task Project_delivery_requires_evidence_and_independent_acceptance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var finding = await service.AddFindingAsync(tenantId, projectId, new ResearchFinding
            {
                Claim = "Users need an approval queue",
                KeyClaim = true,
                Sources = [new("source-a", "interview", "memory"), new("source-b", "survey", "memory")]
            }, CancellationToken.None);
            var brief = await service.CreateBriefAsync(tenantId, projectId, new ProductBrief
            {
                Title = "Approval queue", Problem = finding.Claim,
                MvpScope = ["List", "Decide"], SuccessMetrics = new Dictionary<string, string> { ["decision_success"] = ">=95%" },
                FindingIds = [finding.Id]
            }, CancellationToken.None);
            await service.DecideAsync(tenantId, projectId, brief.Id, "Go", "Evidence supports the scope", [finding.Id], 0, "Controller", CancellationToken.None);
            var requirement = await service.CreateRequirementAsync(tenantId, projectId, new DeliveryRequirement
            {
                Title = "Approval flow", Problem = "Humans need control", Scope = "Tenant dashboard",
                AcceptanceCriteria = ["Owner can approve", "Duplicate decision is rejected"]
            }, CancellationToken.None);
            await service.ReadyRequirementAsync(tenantId, projectId, requirement.Id, 6, CancellationToken.None);
            var taskIds = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
            var plan = await service.ProposePlanAsync(tenantId, projectId, new DecompositionPlan
            {
                RequirementId = requirement.Id, RequirementVersion = 1,
                Tasks = taskIds.Select((id, index) => new DeliveryTask
                {
                    Id = id, Title = $"Delivery task {index}", Weight = 2,
                    RequiredCapabilitiesJson = JsonSerializer.Serialize(new[] { "engineering.implement" }),
                    Author = "Engineer", AcceptanceCriteria = requirement.AcceptanceCriteria
                }).ToArray()
            }, CancellationToken.None);
            await service.ApprovePlanAsync(tenantId, projectId, plan.Id, CancellationToken.None);
            foreach (var taskId in taskIds)
            {
                await service.ClaimTaskAsync(tenantId, projectId, taskId, "Engineer", CancellationToken.None);
                await service.SubmitTaskAsync(tenantId, projectId, taskId, "Engineer", CancellationToken.None);
                await service.AddReviewAsync(tenantId, projectId, new Review
                {
                    SubjectId = taskId, Reviewer = $"Reviewer-{taskId:N}", Author = "Engineer", Verdict = "Approved", Findings = []
                }, CancellationToken.None);
                await service.CompleteTaskAsync(tenantId, projectId, taskId, new DoneEvidence
                {
                    CriteriaResults = requirement.AcceptanceCriteria.ToDictionary(value => value, _ => true),
                    Artifacts = [new($"artifact-{taskId:N}", "document", "local")],
                    TestResults = ["PASS"], SecurityChecks = ["PASS"], OpenP0P1Defects = 0, Validator = $"Reviewer-{taskId:N}"
                }, CancellationToken.None);
            }
            await service.AddAcceptanceReportAsync(tenantId, projectId, new AcceptanceReport
            {
                RequirementId = requirement.Id, Reviewer = "Acceptance A", FunctionalScore = 90,
                UsabilityScore = 90, DesignScore = 85, AccessibilityScore = 80,
                Evidence = [new("a", "report", "local")]
            }, CancellationToken.None);
            await service.AddAcceptanceReportAsync(tenantId, projectId, new AcceptanceReport
            {
                RequirementId = requirement.Id, Reviewer = "Acceptance B", FunctionalScore = 88,
                UsabilityScore = 86, DesignScore = 82, AccessibilityScore = 78,
                Evidence = [new("b", "report", "local")]
            }, CancellationToken.None);

            var beforeRelease = await service.GetAsync(tenantId, projectId, CancellationToken.None);
            Assert.Equal(2, beforeRelease.AcceptanceReports.Count);
            Assert.Equal(2, beforeRelease.AcceptanceReports.Count(report => report.RequirementId == requirement.Id));
            Assert.All(beforeRelease.AcceptanceReports, report => Assert.True(report.Passed));
            Assert.All(beforeRelease.Tasks, task => Assert.Equal("Done", task.Status));
            Assert.Equal(3, beforeRelease.Tasks.Count(task => task.RequirementId == requirement.Id));
            Assert.Equal("Accepted", beforeRelease.Requirements.Single().Status);
            var release = await service.ReleaseAsync(tenantId, projectId, "v1.0.0", "artifact:release", CancellationToken.None);
            var state = await service.GetAsync(tenantId, projectId, CancellationToken.None);

            Assert.Equal("v1.0.0", release.Version);
            Assert.Equal("Accepted", state.Requirements.Single().Status);
            Assert.All(state.Tasks, task => Assert.Equal("Done", task.Status));
            Assert.Equal(3, state.Evidence.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Decision_evidence_and_reround_limits_are_enforced()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-decision-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var finding = await service.AddFindingAsync(tenantId, projectId,
                new ResearchFinding { Claim = "A sourced fact", Sources = [new("source", "web", "local")] }, CancellationToken.None);
            var brief = await service.CreateBriefAsync(tenantId, projectId, new ProductBrief
            {
                Title = "Brief", Problem = "Problem", MvpScope = ["MVP"], SuccessMetrics = new Dictionary<string, string> { ["success"] = "1" },
                FindingIds = [finding.Id]
            }, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(tenantId, projectId, brief.Id,
                "Go", "Rationale", [Guid.NewGuid()], 0, "Controller", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DecideAsync(tenantId, projectId, brief.Id,
                "Go", "Rationale", [finding.Id], 3, "Controller", CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateRequirementAsync(tenantId, projectId,
                new DeliveryRequirement { Title = "Too early" }, CancellationToken.None));

            var decision = await service.DecideAsync(tenantId, projectId, brief.Id, "Go", "Rationale", [finding.Id], 2, "Controller", CancellationToken.None);
            Assert.True(decision.Forced);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Defect_retest_is_limited_to_two_rounds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-retest-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var defect = await service.ReportDefectAsync(tenantId, projectId, new Defect
            {
                Title = "Repeatable defect", ReproductionSteps = "Steps", Severity = "P1"
            }, CancellationToken.None);
            var evidence = new EvidenceRef($"fix:{defect.Id}", "test", "local");

            await service.FixDefectAsync(tenantId, projectId, defect.Id, "Engineer", evidence, CancellationToken.None);
            await service.RetestDefectAsync(tenantId, projectId, defect.Id, false, "Reviewer", CancellationToken.None);
            await service.FixDefectAsync(tenantId, projectId, defect.Id, "Engineer", evidence, CancellationToken.None);
            await service.RetestDefectAsync(tenantId, projectId, defect.Id, false, "Reviewer", CancellationToken.None);
            await service.FixDefectAsync(tenantId, projectId, defect.Id, "Engineer", evidence, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RetestDefectAsync(tenantId, projectId, defect.Id, true, "Reviewer", CancellationToken.None));
            Assert.Equal(2, (await service.GetAsync(tenantId, projectId, CancellationToken.None)).Defects.Single().RetestRound);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Task_executor_cannot_perform_independent_acceptance()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-acceptance-{Guid.NewGuid():N}.json");
        try
        {
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var requirementId = Guid.NewGuid();
            var store = new JsonDocumentStore<ProjectDeliveryState>(path);
            await store.PutAsync(projectId.ToString(), new ProjectDeliveryState
            {
                TenantId = tenantId, ProjectId = projectId,
                Requirements = [new DeliveryRequirement { Id = requirementId, TenantId = tenantId, ProjectId = projectId, Title = "Requirement" }],
                Tasks = [new DeliveryTask { Id = Guid.NewGuid(), TenantId = tenantId, ProjectId = projectId, RequirementId = requirementId, Author = "Engineer", Assignee = "Engineer" }]
            }, CancellationToken.None);
            var service = new DeliveryService(store, TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddAcceptanceReportAsync(tenantId, projectId,
                new AcceptanceReport { RequirementId = requirementId, Reviewer = "Engineer", Evidence = [new("report", "report", "local")] }, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task High_risk_plan_requires_approval_before_tasks_are_created()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-plan-approval-{Guid.NewGuid():N}.json");
        try
        {
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            var requirementId = Guid.NewGuid();
            var plan = new DecompositionPlan
            {
                Id = Guid.NewGuid(), RequirementId = requirementId, RequirementVersion = 1, Status = "Proposed", RiskLevel = "High",
                Tasks = Enumerable.Range(0, 3).Select(_ => new DeliveryTask { Id = Guid.NewGuid(), Title = "Task", Weight = 2 }).ToArray()
            };
            var store = new JsonDocumentStore<ProjectDeliveryState>(path);
            await store.PutAsync(projectId.ToString(), new ProjectDeliveryState
            {
                TenantId = tenantId, ProjectId = projectId,
                Decisions = [new GoNoGoDecision { Decision = "Go" }],
                Requirements = [new DeliveryRequirement { Id = requirementId, TenantId = tenantId, ProjectId = projectId, Status = "Ready", TotalWeight = 6 }],
                Plans = [plan]
            }, CancellationToken.None);
            var service = new DeliveryService(store, TimeProvider.System);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApprovePlanAsync(tenantId, projectId, plan.Id,
                "Controller", null, CancellationToken.None));
            var approved = await service.ApprovePlanAsync(tenantId, projectId, plan.Id, "Controller", "approval-1", CancellationToken.None);

            Assert.Equal("approval-1", approved.ApprovalId);
            Assert.Equal(3, (await service.GetAsync(tenantId, projectId, CancellationToken.None)).Tasks.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Self_review_and_unverified_key_claims_are_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"delivery-{Guid.NewGuid():N}.json");
        try
        {
            var service = new DeliveryService(new JsonDocumentStore<ProjectDeliveryState>(path), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var projectId = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddFindingAsync(tenantId, projectId,
                new ResearchFinding { Claim = "Key claim", KeyClaim = true, Sources = [new("one", "web", "url")] }, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.AddReviewAsync(tenantId, projectId,
                new Review { Reviewer = "Same", Author = "Same", Verdict = "Approved" }, CancellationToken.None));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
