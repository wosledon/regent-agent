using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RegentAgent.ControlPlane.Tests;

internal sealed class FaultInjectingSaveChangesInterceptor : SaveChangesInterceptor
{
    public bool FailOnSaveChanges { get; set; }
    public string FailureMessage { get; set; } = "Injected failure before commit.";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailOnSaveChanges) throw new InvalidOperationException(FailureMessage);
        return ValueTask.FromResult(result);
    }
}
