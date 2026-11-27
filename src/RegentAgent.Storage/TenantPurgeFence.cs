using System.Collections.Concurrent;

namespace RegentAgent.Storage;

public sealed class TenantPurgeInProgressException(string tenantId)
    : InvalidOperationException($"Tenant {tenantId} is being purged.");

public interface ITenantPurgeOperationLease : IDisposable
{
    void AbortBeforeDurableState();
}

public sealed class TenantPurgeFence
{
    private static readonly ConcurrentDictionary<Guid, TenantFenceState> States = new();
    private static readonly AsyncLocal<TenantFenceScope?> Current = new();

    public static bool IsPurging(Guid tenantId) => States.TryGetValue(tenantId, out var state) && state.IsPurging;

    internal static void Reset(Guid tenantId) => States.TryRemove(tenantId, out _);

    internal static int GetActiveWrites(Guid tenantId) =>
        States.TryGetValue(tenantId, out var state) ? state.ActiveWrites : 0;

    public static bool HasWriteScope(Guid tenantId) => Current.Value is { } scope && scope.TenantId == tenantId;

    public static Task<IDisposable> EnterWriteAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Current.Value is { } scope && scope.TenantId == tenantId)
            return Task.FromResult<IDisposable>(new TenantFenceNoopLease());

        var state = States.GetOrAdd(tenantId, _ => new TenantFenceState());
        lock (state.Sync)
        {
            if (state.IsPurging) throw new TenantPurgeInProgressException(tenantId.ToString());
            if (state.ActiveWrites == 0) state.Drain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            state.ActiveWrites++;
        }

        var entered = new TenantFenceScope(tenantId, state, tracksWrite: true);
        Current.Value = entered;
        return Task.FromResult<IDisposable>(new TenantFenceLease(entered));
    }

    public static async Task CheckPersistedPurgeAsync(Guid tenantId, CancellationToken cancellationToken,
        ITenantPurgeStateProvider? purgeState)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Current.Value is { } scope && scope.TenantId == tenantId) return;

        var state = States.GetOrAdd(tenantId, _ => new TenantFenceState());
        lock (state.Sync)
        {
            if (state.IsPurging) throw new TenantPurgeInProgressException(tenantId.ToString());
        }

        if (purgeState is not null && await purgeState.IsPurgeActiveAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            lock (state.Sync) state.IsPurging = true;
            throw new TenantPurgeInProgressException(tenantId.ToString());
        }

        lock (state.Sync)
        {
            if (state.IsPurging) throw new TenantPurgeInProgressException(tenantId.ToString());
        }
    }

    public static async Task<ITenantPurgeOperationLease> BeginPurgeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Current.Value is { } scope && scope.TenantId == tenantId)
            throw new InvalidOperationException("A tenant write cannot start its own purge.");

        var state = States.GetOrAdd(tenantId, _ => new TenantFenceState());
        Task drain;
        lock (state.Sync)
        {
            if (state.PurgeActive) throw new InvalidOperationException("Tenant purge is already in progress.");
            state.IsPurging = true;
            state.PurgeActive = true;
            drain = state.ActiveWrites == 0 ? Task.CompletedTask : state.Drain.Task;
        }

        try
        {
            while (!drain.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await drain.WaitAsync(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            await drain.ConfigureAwait(false);
            return new TenantPurgeOperationLease(state);
        }
        catch
        {
            lock (state.Sync)
            {
                state.PurgeActive = false;
                state.IsPurging = false;
            }
            throw;
        }
    }

    public static IDisposable AllowPurgeWrites(Guid tenantId)
    {
        var state = States.GetOrAdd(tenantId, _ => new TenantFenceState());
        lock (state.Sync)
        {
            if (!state.IsPurging) throw new InvalidOperationException("Tenant purge writes require a durable purge state.");
        }
        var previous = Current.Value;
        var scope = new TenantFenceScope(tenantId, state, tracksWrite: false);
        Current.Value = scope;
        return new TenantFenceScopeLease(scope, previous);
    }

    private sealed class TenantFenceState
    {
        public object Sync { get; } = new();
        public bool IsPurging { get; set; }
        public bool PurgeActive { get; set; }
        public int ActiveWrites { get; set; }
        public TaskCompletionSource Drain { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TenantFenceScope(Guid tenantId, TenantFenceState state, bool tracksWrite)
    {
        public Guid TenantId { get; } = tenantId;
        public TenantFenceState State { get; } = state;
        public bool TracksWrite { get; } = tracksWrite;
        public bool Disposed { get; set; }
    }

    private sealed class TenantFenceNoopLease : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class TenantPurgeOperationLease(TenantFenceState state) : ITenantPurgeOperationLease
    {
        private bool disposed;
        public void AbortBeforeDurableState()
        {
            if (disposed) return;
            disposed = true;
            lock (state.Sync)
            {
                state.PurgeActive = false;
                state.IsPurging = false;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (state.Sync) state.PurgeActive = false;
        }
    }

    private sealed class TenantFenceLease(TenantFenceScope scope) : IDisposable
    {
        public void Dispose()
        {
            if (scope.Disposed) return;
            scope.Disposed = true;
            lock (scope.State.Sync)
            {
                scope.State.ActiveWrites--;
                if (scope.State.ActiveWrites == 0) scope.State.Drain.TrySetResult();
            }
            if (ReferenceEquals(Current.Value, scope)) Current.Value = null;
        }
    }

    private sealed class TenantFenceScopeLease(TenantFenceScope scope, TenantFenceScope? previous) : IDisposable
    {
        public void Dispose()
        {
            if (scope.Disposed) return;
            scope.Disposed = true;
            if (ReferenceEquals(Current.Value, scope)) Current.Value = previous;
        }
    }
}
