using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.AgentIdentity;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Application.AgentTools;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Artifacts;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class RuntimeDeliveryIntegrationTests
{
    [Fact]
    public async Task Runtime_claim_submits_through_agent_tool_and_completes_with_stable_assignee()
    {
        using var factory = new ControlPlaneRuntimeFactory("Development", "InMemory", true);
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "runtime-delivery", CompanyName = "Runtime Delivery Co" });
        db.Employees.Add(new EmployeeRecord
        {
            Id = employeeId, TenantId = tenantId, EmployeeNo = "EMP-0001", DisplayName = "Engineer",
            Position = "Engineer", State = EmployeeState.Active
        });
        db.Positions.Add(new PositionRecord
        {
            Id = positionId, TenantId = tenantId, Name = "Engineer", Responsibilities = "Implement delivery tasks",
            CapabilitiesJson = JsonSerializer.Serialize(new[] { "engineering.implement" }), Status = "Filled"
        });
        db.EmploymentPeriods.Add(new EmploymentPeriodRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, PositionId = positionId,
            Kind = "Primary", StartedAt = DateTimeOffset.UtcNow
        });
        db.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = "Runtime delivery", Owner = "Controller",
            Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack, Currency = "CNY",
            TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var delivery = scope.ServiceProvider.GetRequiredService<DeliveryService>();
        var projection = scope.ServiceProvider.GetRequiredService<DeliveryRuntimeProjectionService>();
        var finding = await delivery.AddFindingAsync(tenantId, projectId, new ResearchFinding
        {
            Claim = "Runtime identity must survive delivery transitions",
            Sources = [new("source-a", "interview", "memory"), new("source-b", "survey", "memory")]
        }, CancellationToken.None);
        var brief = await delivery.CreateBriefAsync(tenantId, projectId, new ProductBrief
        {
            Title = "Stable assignee", Problem = finding.Claim, MvpScope = ["Preserve employee ID"],
            SuccessMetrics = new Dictionary<string, string> { ["identity"] = "stable" }, FindingIds = [finding.Id]
        }, CancellationToken.None);
        await delivery.DecideAsync(tenantId, projectId, brief.Id, "Go", "Evidence supports the scope",
            [finding.Id], 0, "Controller", CancellationToken.None);
        var requirement = await delivery.CreateRequirementAsync(tenantId, projectId, new DeliveryRequirement
        {
            Title = "Stable runtime assignee", Problem = "Display names are not identity", Scope = "Runtime projection",
            AcceptanceCriteria = ["Works"]
        }, CancellationToken.None);
        await delivery.ReadyRequirementAsync(tenantId, projectId, requirement.Id, 6, CancellationToken.None);
        var plan = await delivery.ProposePlanAsync(tenantId, projectId, new DecompositionPlan
        {
            RequirementId = requirement.Id, RequirementVersion = 1,
            Tasks = Enumerable.Range(0, 3).Select(index => new DeliveryTask
            {
                Id = Guid.NewGuid(), Title = $"Runtime task {index}", Weight = 2, Assignee = "Engineer",
                TaskType = "engineering.implement", RequiredCapabilitiesJson = JsonSerializer.Serialize(new[] { "engineering.implement" }),
                AcceptanceCriteria = requirement.AcceptanceCriteria
            }).ToArray()
        }, "Controller", CancellationToken.None);
        await delivery.ApprovePlanAsync(tenantId, projectId, plan.Id, CancellationToken.None);
        await projection.SyncApprovedPlanAsync(tenantId, projectId, plan, CancellationToken.None);

        var claim = await scope.ServiceProvider.GetRequiredService<TaskExecutionService>()
            .ClaimAsync("runner-test", tenantId, CancellationToken.None);
        Assert.NotNull(claim);
        var claimed = (await delivery.GetAsync(tenantId, projectId, CancellationToken.None))
            .Tasks.Single(task => task.Id == claim.TaskId);
        Assert.Equal("Claimed", claimed.Status);
        Assert.Equal(employeeId, claimed.AssigneeEmployeeId);

        var identity = scope.ServiceProvider.GetRequiredService<AgentAssignmentTokenValidator>().Validate(claim.AssignmentToken);
        var handler = scope.ServiceProvider.GetServices<IAgentToolHandler>().Single(candidate => candidate.ToolName == "project");
        var context = new AgentToolContext(identity, "Engineer", "{}", "Low", "{}");
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            operation = "submit_task", projectId, taskId = claim.TaskId
        }));
        await handler.ExecuteAsync(context, arguments.RootElement, CancellationToken.None);

        var submitted = (await delivery.GetAsync(tenantId, projectId, CancellationToken.None))
            .Tasks.Single(task => task.Id == claim.TaskId);
        Assert.Equal("InReview", submitted.Status);
        Assert.Equal(employeeId, submitted.AssigneeEmployeeId);
        Assert.Equal(employeeId, (await db.Tasks.SingleAsync(task => task.Id == claim.TaskId)).AssigneeEmployeeId);

        const string reviewer = "Independent Reviewer";
        await delivery.AddReviewAsync(tenantId, projectId, new Review
        {
            SubjectId = claim.TaskId, Reviewer = reviewer, Verdict = "Approved", Findings = []
        }, CancellationToken.None);
        var artifactService = new ArtifactService(db, new RuntimeTestTenantContext(tenantId),
            scope.ServiceProvider.GetRequiredService<IObjectStore>(), TimeProvider.System,
            scope.ServiceProvider.GetRequiredService<ITenantPurgeStateProvider>());
        ArtifactDto artifact;
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("independent delivery artifact")))
            artifact = await artifactService.UploadAsync(projectId, claim.TaskId, content, "text/plain", content.Length, CancellationToken.None);
        await delivery.CompleteTaskAsync(tenantId, projectId, claim.TaskId, new DoneEvidence
        {
            CriteriaResults = new Dictionary<string, bool> { ["Works"] = true },
            Artifacts = [new(artifact.Id.ToString(), "artifact", artifact.ObjectKey, artifact.Hash)],
            TestResults = [new("unit", true, "test:unit")],
            SecurityChecks = [new("tenant isolation", true, "test:security")],
            OpenP0P1Defects = 0, Validator = reviewer
        }, CancellationToken.None);
        await projection.SyncRuntimeStatusAsync(tenantId, claim.TaskId, "Done", string.Empty, CancellationToken.None);

        var completed = (await delivery.GetAsync(tenantId, projectId, CancellationToken.None))
            .Tasks.Single(task => task.Id == claim.TaskId);
        Assert.Equal("Done", completed.Status);
        Assert.Equal(employeeId, completed.AssigneeEmployeeId);
        Assert.Equal(employeeId, (await db.Tasks.SingleAsync(task => task.Id == claim.TaskId)).AssigneeEmployeeId);
    }
}
