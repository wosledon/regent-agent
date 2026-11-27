using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegentAgent.ControlPlane.Data;
using RegentAgent.Contracts.Enums;
using RegentAgent.Contracts.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

[Collection("ControlPlane HTTP security")]
public sealed class DeliveryEvidenceApiTests
{
    [Fact]
    public async Task Review_and_done_evidence_use_authenticated_reviewer_and_authoritative_artifact()
    {
        using var factory = new ControlPlaneSecurityHttpFactory();
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RegentDbContext>();
        var objects = scope.ServiceProvider.GetRequiredService<IObjectStore>();
        db.Tenants.Add(new TenantRecord { Id = tenantId, Name = "delivery-evidence", CompanyName = "Delivery Evidence Co" });
        db.Projects.Add(new ProjectRecord
        {
            Id = projectId, TenantId = tenantId, Name = "Evidence round trip", Owner = "Owner",
            Stage = ProjectStage.Active, Health = ProjectHealth.OnTrack, Currency = "CNY",
            TargetAt = DateTimeOffset.UtcNow.AddDays(1), UpdatedAt = DateTimeOffset.UtcNow
        });
        var objectKey = $"tenants/{tenantId}/projects/{projectId}/artifacts/{Guid.NewGuid()}";
        var artifactId = Guid.NewGuid();
        StoredObject stored;
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("synthetic delivery artifact")))
            stored = await objects.PutAsync(new ObjectUploadRequest(objectKey, content, "text/plain"), CancellationToken.None);
        db.Artifacts.Add(new ArtifactRecord
        {
            Id = artifactId, TenantId = tenantId, ProjectId = projectId, TaskId = taskId,
            ObjectKey = objectKey, Hash = stored.Sha256, ContentType = stored.ContentType,
            Length = stored.Length, UploadState = "Ready", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var store = new JsonDocumentStore<ProjectDeliveryState>(
            Path.Combine(factory.Root, "data", "modules", "project-delivery.json"));
        await store.PutAsync(projectId.ToString(), new ProjectDeliveryState
        {
            TenantId = tenantId,
            ProjectId = projectId,
            Tasks =
            [
                new DeliveryTask
                {
                    Id = taskId, TenantId = tenantId, ProjectId = projectId, Status = "InReview",
                    Author = actorId.ToString(), Assignee = "Assignee", AcceptanceCriteria = ["Works"]
                }
            ]
        }, CancellationToken.None);

        using (var artifactsRequest = new HttpRequestMessage(HttpMethod.Get,
                   $"/v1/projects/{projectId}/delivery/tasks/{taskId}/artifacts"))
        {
            TestIdentityRequest.Apply(artifactsRequest, tenantId, role: "Owner", userId: actorId);
            using var artifactsResponse = await client.SendAsync(artifactsRequest);
            var artifactsBody = await artifactsResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, artifactsResponse.StatusCode);
            using var artifacts = JsonDocument.Parse(artifactsBody);
            var artifact = Assert.Single(artifacts.RootElement.EnumerateArray());
            Assert.Equal(artifactId, artifact.GetProperty("id").GetGuid());
            Assert.Equal(objectKey, artifact.GetProperty("objectKey").GetString());
            Assert.Equal(stored.Sha256, artifact.GetProperty("hash").GetString());
        }

        using (var selfReview = new HttpRequestMessage(HttpMethod.Post,
                   $"/v1/projects/{projectId}/delivery/reviews")
               {
                   Content = JsonContent.Create(new
                   {
                       subjectId = taskId, subjectType = "Task", reviewer = actorId,
                       verdict = "Approved", findings = Array.Empty<string>(), evidence = Array.Empty<object>()
                   })
               })
        {
            selfReview.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            TestIdentityRequest.Apply(selfReview, tenantId, role: "Owner", userId: actorId);
            using var selfReviewResponse = await client.SendAsync(selfReview);
            Assert.Equal(HttpStatusCode.Conflict, selfReviewResponse.StatusCode);
        }

        using (var review = new HttpRequestMessage(HttpMethod.Post,
                   $"/v1/projects/{projectId}/delivery/reviews")
               {
                   Content = JsonContent.Create(new
                   {
                       subjectId = taskId, subjectType = "Task", reviewer = reviewerId,
                       verdict = "Approved", findings = Array.Empty<string>(),
                       evidence = new[] { new { id = artifactId, kind = "artifact", location = objectKey, sha256 = stored.Sha256 } }
                   })
               })
        {
            review.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            TestIdentityRequest.Apply(review, tenantId, role: "CoOwner", userId: reviewerId);
            using var reviewResponse = await client.SendAsync(review);
            var reviewBody = await reviewResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
            using var reviewed = JsonDocument.Parse(reviewBody);
            Assert.Equal(reviewerId, reviewed.RootElement.GetProperty("reviewer").GetGuid());
            Assert.Equal(actorId.ToString(), reviewed.RootElement.GetProperty("author").GetString());
        }

        HttpRequestMessage CreateCompletion(string hash, string idempotencyKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"/v1/projects/{projectId}/delivery/tasks/{taskId}/complete")
            {
                Content = JsonContent.Create(new
                {
                    workItemId = taskId,
                    workItemType = "Task",
                    criteriaResults = new Dictionary<string, bool> { ["Works"] = true },
                    artifacts = new[] { new { id = artifactId, kind = "test", location = objectKey, sha256 = hash } },
                    testResults = new[] { new { name = "unit", passed = true, evidenceRef = "test:unit" } },
                    securityChecks = new[] { new { name = "tenant isolation", passed = true, evidenceRef = "test:security" } },
                    openP0P1Defects = 0,
                    validator = reviewerId
                })
            };
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            TestIdentityRequest.Apply(request, tenantId, role: "CoOwner", userId: reviewerId);
            return request;
        }

        using (var rejected = CreateCompletion(new string('0', 64), Guid.NewGuid().ToString()))
        {
            using var rejectedResponse = await client.SendAsync(rejected);
            Assert.Equal(HttpStatusCode.Conflict, rejectedResponse.StatusCode);
        }
        Assert.Equal("InReview", (await store.GetAsync(projectId.ToString(), CancellationToken.None))!
            .Tasks.Single().Status);

        using var complete = CreateCompletion(stored.Sha256, Guid.NewGuid().ToString());
        using var completeResponse = await client.SendAsync(complete);
        var completedBody = await completeResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        using var completed = JsonDocument.Parse(completedBody);
        Assert.Equal(reviewerId, completed.RootElement.GetProperty("validator").GetGuid());
        AssertCheck(completed.RootElement.GetProperty("testResults")[0], "unit", "test:unit");
        AssertCheck(completed.RootElement.GetProperty("securityChecks")[0], "tenant isolation", "test:security");

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/v1/projects/{projectId}/delivery");
        TestIdentityRequest.Apply(get, tenantId, role: "Owner", userId: actorId);
        using var getResponse = await client.SendAsync(get);
        var deliveryBody = await getResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var delivery = JsonDocument.Parse(deliveryBody);
        var evidence = delivery.RootElement.GetProperty("evidence").EnumerateArray().Single();
        AssertCheck(evidence.GetProperty("testResults")[0], "unit", "test:unit");
        AssertCheck(evidence.GetProperty("securityChecks")[0], "tenant isolation", "test:security");

        var reloaded = await new JsonDocumentStore<ProjectDeliveryState>(
            Path.Combine(factory.Root, "data", "modules", "project-delivery.json"))
            .GetAsync(projectId.ToString(), CancellationToken.None);
        var persisted = Assert.Single(reloaded!.Evidence);
        Assert.Equal(reviewerId.ToString(), persisted.Validator);
        Assert.Equal("unit", Assert.Single(persisted.TestResults).Name);
        Assert.Equal("tenant isolation", Assert.Single(persisted.SecurityChecks).Name);
    }

    private static void AssertCheck(JsonElement check, string name, string evidenceRef)
    {
        Assert.Equal(JsonValueKind.Object, check.ValueKind);
        Assert.Equal(name, check.GetProperty("name").GetString());
        Assert.True(check.GetProperty("passed").GetBoolean());
        Assert.Equal(evidenceRef, check.GetProperty("evidenceRef").GetString());
    }
}
