using RegentAgent.Agents;
using RegentAgent.Contracts.Agents;
using RegentAgent.Contracts.Projects;
using RegentAgent.Projects;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class TaskGovernanceServiceTests
{
    [Fact]
    public async Task Tool_loop_wip_ledger_and_review_quorum_are_enforced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"task-governance-{Guid.NewGuid():N}");
        try
        {
            var runtime = new AgentRuntimeService(new JsonDocumentStore<AgentContextSnapshot>(Path.Combine(root, "contexts.json")), TimeProvider.System);
            var tenantId = Guid.NewGuid();
            var taskId = Guid.NewGuid();
            await runtime.BuildContextAsync(tenantId, taskId, "system", "memory", "{}", [], "{}", false, CancellationToken.None);
            var loop = await runtime.RunToolLoopAsync(tenantId, new ToolLoopRequest
            {
                TaskId = taskId, MaxSteps = 3, CostWarningThreshold = 1m,
                Steps = [
                    new() { ToolName = "search", Capability = "research", Authorized = true, Result = "ok", Cost = 2m },
                    new() { ToolName = "search", Capability = "research", Authorized = true, Result = "duplicate", Cost = 0m },
                    new() { ToolName = "admin", Capability = "org.update", Authorized = false, Result = "denied", Cost = 0m }
                ]
            }, CancellationToken.None);
            var service = new TaskGovernanceService(new JsonDocumentStore<TaskGovernanceState>(Path.Combine(root, "state.json")), TimeProvider.System);
            var employeeId = Guid.NewGuid();
            await service.RegisterTaskTypeAsync(tenantId, new TaskTypeDefinition { TaskType = "engineering.implement" }, CancellationToken.None);
            await service.AssessAsync(tenantId, new CapabilityAssessment { EmployeeId = employeeId, Dimension = "engineering", ModelScore = 0.9m, HistoryScore = 0.8m, ToolFit = 1m, ContextFit = 1m }, CancellationToken.None);
            var claim = await service.ScoreClaimAsync(tenantId, employeeId, "engineering.implement", 1, 3, 0.9m, 1m, 0.8m, true, CancellationToken.None);
            var assignment = await service.AutoAssignAsync(tenantId, new AutoAssignRequest(taskId, "engineering.implement",
                [new AutoAssignCandidate { EmployeeId = employeeId, CurrentWip = 1, WipLimit = 3, HistorySuccess = 0.9m, ToolFit = 1m, CostEfficiency = 0.8m }]), CancellationToken.None);
            var duplicateAssignment = await service.AutoAssignAsync(tenantId, new AutoAssignRequest(taskId, "engineering.implement",
                [new AutoAssignCandidate { EmployeeId = Guid.NewGuid(), CurrentWip = 0, WipLimit = 3, HistorySuccess = 1m, ToolFit = 1m, CostEfficiency = 1m }]), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReassignTaskAsync(tenantId, taskId, Guid.NewGuid(), "Workload", "", CancellationToken.None));
            var replacementId = Guid.NewGuid();
            var reassigned = await service.ReassignTaskAsync(tenantId, taskId, replacementId, "Workload", "approval-1", CancellationToken.None);
            var ledger = await service.RecordWorkAsync(tenantId, new WorkLedgerEntry { EmployeeId = employeeId, TaskId = taskId, TaskType = "engineering.implement", TaskWeight = 3 }, CancellationToken.None);
            var duplicate = await service.RecordWorkAsync(tenantId, new WorkLedgerEntry { EmployeeId = employeeId, TaskId = taskId, TaskType = "engineering.implement", TaskWeight = 3 }, CancellationToken.None);
            var review = await service.CreateReviewAsync(tenantId, new ReviewTaskRecord { SubjectId = taskId, Author = "Engineer", RequiredQuorum = 2 }, CancellationToken.None);
            await service.ApproveReviewAsync(tenantId, review.Id, "Reviewer A", CancellationToken.None);
            var approved = await service.ApproveReviewAsync(tenantId, review.Id, "Reviewer B", CancellationToken.None);

            Assert.Equal(1, loop.DuplicateSteps);
            Assert.Equal(1, loop.UnauthorizedSteps);
            Assert.True(loop.ReviewRequired);
            Assert.True(claim.Eligible);
            Assert.True(assignment.Assigned);
            Assert.Equal(employeeId, assignment.EmployeeId);
            Assert.Equal(employeeId, duplicateAssignment.EmployeeId);
            Assert.Equal(replacementId, reassigned.EmployeeId);
            Assert.Equal(employeeId, reassigned.PreviousEmployeeId);
            Assert.Equal("approval-1", reassigned.ApprovalId);
            Assert.Equal(ledger.Id, duplicate.Id);
            Assert.Equal("Approved", approved.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
