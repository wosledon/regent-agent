using RegentAgent.Performance;
using RegentAgent.Storage;
using Xunit;

namespace RegentAgent.ControlPlane.Tests;

public sealed class PerformanceGovernanceTests
{
    [Fact]
    public async Task Improvement_author_cannot_self_review_and_approval_does_not_grant_permissions()
    {
        var root = Path.Combine(Path.GetTempPath(), $"improvement-{Guid.NewGuid():N}");
        try
        {
            var service = new ImprovementService(new JsonDocumentStore<ImprovementProposal>(Path.Combine(root, "proposals.json")));
            var proposal = await service.ProposeAsync("tenant-a", "Engineer", "tool", "Automate checks", "Reduce review time", CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewAsync(proposal.Id, "tenant-a", true,
                "Engineer", "Self review", CancellationToken.None));
            var approved = await service.ReviewAsync(proposal.Id, "tenant-a", true, "Leader", "Safe to pilot", CancellationToken.None);

            Assert.Equal("Approved", approved.Status);
            Assert.Equal("Leader", approved.ReviewedBy);
            Assert.Equal("Safe to pilot", approved.ReviewNote);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Performance_appeal_requires_independent_resolution_and_can_reverse_action()
    {
        var root = Path.Combine(Path.GetTempPath(), $"performance-{Guid.NewGuid():N}");
        try
        {
            var service = new PerformanceActionService(new JsonDocumentStore<PerformanceActionRecord>(Path.Combine(root, "actions.json")));
            var action = await service.PerformAsync("tenant-a", "Engineer", "Coaching", null, "Missed checkpoint", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.PerformAsync("tenant-a", "Engineer", "Terminate", null, "Reason", CancellationToken.None));

            await service.AppealAsync(action.Id, "tenant-a", "Checkpoint was unavailable", CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveAppealAsync(action.Id, "tenant-a", "", "Missing reviewer", true, CancellationToken.None));
            var resolved = await service.ResolveAppealAsync(action.Id, "tenant-a", "Independent Reviewer", "Evidence supports employee", false, CancellationToken.None);

            Assert.Equal("Overturned", resolved.AppealStatus);
            Assert.Equal("Reversed", resolved.Status);
            Assert.Equal("Independent Reviewer", resolved.AppealReviewer);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
