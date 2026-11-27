using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Application;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Artifacts;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class DeliveryConcurrencyTests
{
    [Fact]
    public async Task Scoped_delivery_mutations_do_not_lose_findings_reviews_or_evidence()
    {
        using var factory = new ControlPlaneRuntimeFactory("Development", "InMemory", true);
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        Guid[] taskIds = [];
        Dictionary<Guid, ArtifactDto> artifacts = [];

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
            db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "concurrency", CompanyName = "Concurrency Co" });
            db.Projects.Add(new ProjectRecord
            {
                Id = projectId, TenantId = tenantId, Name = "Concurrent delivery", Owner = "Controller",
                Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack, Currency = "CNY",
                TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();

            var delivery = scope.ServiceProvider.GetRequiredService<DeliveryService>();
            var finding = await delivery.AddFindingAsync(tenantId, projectId, new ResearchFinding
            {
                Claim = "Concurrent mutations must be durable",
                Sources = [new("source-a", "interview", "memory"), new("source-b", "survey", "memory")]
            }, CancellationToken.None);
            var brief = await delivery.CreateBriefAsync(tenantId, projectId, new ProductBrief
            {
                Title = "Concurrency", Problem = finding.Claim, MvpScope = ["Atomic writes"],
                SuccessMetrics = new Dictionary<string, string> { ["durability"] = "100%" }, FindingIds = [finding.Id]
            }, CancellationToken.None);
            await delivery.DecideAsync(tenantId, projectId, brief.Id, "Go", "Evidence supports atomic delivery",
                [finding.Id], 0, "Controller", CancellationToken.None);
            var requirement = await delivery.CreateRequirementAsync(tenantId, projectId, new DeliveryRequirement
            {
                Title = "Atomic delivery", Problem = "Avoid lost updates", Scope = "Delivery module",
                AcceptanceCriteria = ["Works"]
            }, CancellationToken.None);
            await delivery.ReadyRequirementAsync(tenantId, projectId, requirement.Id, 6, CancellationToken.None);
            var plan = await delivery.ProposePlanAsync(tenantId, projectId, new DecompositionPlan
            {
                RequirementId = requirement.Id, RequirementVersion = 1,
                Tasks = Enumerable.Range(0, 3).Select(index => new DeliveryTask
                {
                    Id = Guid.NewGuid(), Title = $"Task {index}", Weight = 2,
                    TaskType = "engineering.implement", RequiredCapabilitiesJson = JsonSerializer.Serialize(new[] { "engineering.implement" }),
                    AcceptanceCriteria = requirement.AcceptanceCriteria
                }).ToArray()
            }, "Controller", CancellationToken.None);
            await delivery.ApprovePlanAsync(tenantId, projectId, plan.Id, CancellationToken.None);
            taskIds = plan.Tasks.Select(task => task.Id).ToArray();
            await scope.ServiceProvider.GetRequiredService<DeliveryRuntimeProjectionService>()
                .SyncApprovedPlanAsync(tenantId, projectId, plan, CancellationToken.None);
            foreach (var taskId in taskIds)
            {
                await delivery.ClaimTaskAsync(tenantId, projectId, taskId, "Engineer", CancellationToken.None);
                await delivery.SubmitTaskAsync(tenantId, projectId, taskId, "Engineer", CancellationToken.None);
            }

            var artifactService = new ArtifactService(db, new RuntimeTestTenantContext(tenantId),
                scope.ServiceProvider.GetRequiredService<IObjectStore>(), TimeProvider.System,
                scope.ServiceProvider.GetRequiredService<ITenantPurgeStateProvider>());
            foreach (var taskId in taskIds.Take(2))
            {
                await using var content = new MemoryStream(Encoding.UTF8.GetBytes($"artifact for {taskId}"));
                artifacts[taskId] = await artifactService.UploadAsync(projectId, taskId, content, "text/plain", content.Length, CancellationToken.None);
            }
        }

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<DeliveryService>();
        var second = secondScope.ServiceProvider.GetRequiredService<DeliveryService>();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? first : second).AddFindingAsync(tenantId, projectId,
                new ResearchFinding { Claim = $"Finding {index}", Sources = [new($"source-{index}", "web", "memory")] },
                CancellationToken.None)));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            (index % 2 == 0 ? first : second).AddReviewAsync(tenantId, projectId, new Review
            {
                SubjectId = taskIds[index % 2], Reviewer = $"Reviewer-{index}", Verdict = "Approved", Findings = []
            }, CancellationToken.None)));
        await Task.WhenAll(
            CompleteAsync(first, tenantId, projectId, taskIds[0], artifacts[taskIds[0]], "Reviewer-0"),
            CompleteAsync(second, tenantId, projectId, taskIds[1], artifacts[taskIds[1]], "Reviewer-1"));

        await using var verificationScope = factory.Services.CreateAsyncScope();
        var state = await verificationScope.ServiceProvider.GetRequiredService<DeliveryService>()
            .GetAsync(tenantId, projectId, CancellationToken.None);
        Assert.Equal(21, state.Findings.Count);
        Assert.Equal(20, state.Reviews.Count);
        Assert.Equal(2, state.Evidence.Count);
        Assert.Equal("Done", state.Tasks.Single(task => task.Id == taskIds[0]).Status);
        Assert.Equal("Done", state.Tasks.Single(task => task.Id == taskIds[1]).Status);
    }

    private static Task<DoneEvidence> CompleteAsync(DeliveryService delivery, Guid tenantId, Guid projectId,
        Guid taskId, ArtifactDto artifact, string validator) => delivery.CompleteTaskAsync(tenantId, projectId, taskId,
        new DoneEvidence
        {
            CriteriaResults = new Dictionary<string, bool> { ["Works"] = true },
            Artifacts = [new(artifact.Id.ToString(), "artifact", artifact.ObjectKey, artifact.Hash)],
            TestResults = [new("unit", true, "test:unit")],
            SecurityChecks = [new("tenant isolation", true, "test:security")],
            OpenP0P1Defects = 0, Validator = validator
        }, CancellationToken.None);
}
