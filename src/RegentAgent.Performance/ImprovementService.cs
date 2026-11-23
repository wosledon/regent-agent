using RegentAgent.Storage;

namespace RegentAgent.Performance;

public sealed record ImprovementProposal(string Id, string TenantId, string EmployeeId, string Category,
    string Summary, string ExpectedBenefit, string Status, DateTimeOffset CreatedAt,
    string? ReviewedBy = null, string? ReviewNote = null, DateTimeOffset? ReviewedAt = null);

public sealed class ImprovementService(JsonDocumentStore<ImprovementProposal> proposals)
{
    public async Task<ImprovementProposal> ProposeAsync(string tenantId, string employeeId, string category,
        string summary, string expectedBenefit, CancellationToken cancellationToken)
    {
        if (category is not ("process" or "skill" or "tool" or "model" or "organization"))
            throw new InvalidOperationException("Invalid improvement category.");
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(expectedBenefit))
            throw new InvalidOperationException("Employee, summary and expected benefit are required.");
        var proposal = new ImprovementProposal(Guid.NewGuid().ToString("N"), tenantId, employeeId, category,
            summary, expectedBenefit, "Proposed", DateTimeOffset.UtcNow);
        await proposals.PutAsync(proposal.Id, proposal, cancellationToken);
        return proposal;
    }

    public async Task<IReadOnlyList<ImprovementProposal>> ListAsync(string tenantId, CancellationToken cancellationToken) =>
        (await proposals.GetAllAsync(cancellationToken)).Where(x => x.TenantId == tenantId).ToArray();

    public async Task<ImprovementProposal> ReviewAsync(string id, string tenantId, bool approved,
        string reviewer, string note, CancellationToken cancellationToken)
    {
        var proposal = await proposals.GetAsync(id, cancellationToken) ?? throw new KeyNotFoundException("Proposal not found.");
        if (proposal.TenantId != tenantId) throw new UnauthorizedAccessException();
        if (proposal.Status != "Proposed") throw new InvalidOperationException("Proposal has already been reviewed.");
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(note)) throw new InvalidOperationException("Reviewer and note are required.");
        if (reviewer == proposal.EmployeeId) throw new InvalidOperationException("Proposal author cannot review their own improvement.");
        proposal = proposal with
        {
            Status = approved ? "Approved" : "Rejected", ReviewedBy = reviewer,
            ReviewNote = note, ReviewedAt = DateTimeOffset.UtcNow
        };
        await proposals.PutAsync(id, proposal, cancellationToken);
        return proposal;
    }
}
