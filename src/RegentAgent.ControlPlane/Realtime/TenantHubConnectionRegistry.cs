using System.Collections.Concurrent;

namespace RegentAgent.ControlPlane.Realtime;

public sealed record TenantHubConnection(
    Guid SessionId,
    Guid TenantId,
    Guid UserId,
    string? EmployeeId,
    Action TerminateConnection,
    Guid? CredentialEpoch = null);

public sealed class TenantHubConnectionRegistry
{
    private readonly ConcurrentDictionary<string, TenantHubConnection> connections = new();

    public void Connected(string connectionId, TenantHubConnection connection) => connections[connectionId] = connection;
    public bool TryGet(string connectionId, out TenantHubConnection? connection) =>
        connections.TryGetValue(connectionId, out connection);
    public void Disconnected(string connectionId) => connections.TryRemove(connectionId, out _);

    public async Task DisconnectSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        foreach (var (connectionId, _) in connections.Where(x => x.Value.SessionId == sessionId).ToArray())
            await DisconnectConnectionAsync(connectionId, cancellationToken);
    }

    public Task DisconnectEmployeeAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken) =>
        DisconnectEmployeeAsync(tenantId, employeeId, null, cancellationToken);

    public async Task DisconnectEmployeeAsync(Guid tenantId, Guid employeeId, Guid? credentialEpoch,
        CancellationToken cancellationToken)
    {
        foreach (var (connectionId, _) in connections.Where(x => x.Value.TenantId == tenantId
                     && string.Equals(x.Value.EmployeeId, employeeId.ToString(), StringComparison.OrdinalIgnoreCase)
                     && (credentialEpoch is null || x.Value.CredentialEpoch == credentialEpoch)).ToArray())
            await DisconnectConnectionAsync(connectionId, cancellationToken);
    }

    public async Task DisconnectUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        foreach (var (connectionId, _) in connections.Where(x => x.Value.TenantId == tenantId && x.Value.UserId == userId).ToArray())
            await DisconnectConnectionAsync(connectionId, cancellationToken);
    }

    public IReadOnlyList<(string ConnectionId, TenantHubConnection Connection)> Snapshot() =>
        connections.Select(x => (x.Key, x.Value)).ToArray();

    public Task DisconnectConnectionAsync(string connectionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!connections.TryGetValue(connectionId, out var connection)) return Task.CompletedTask;
        try
        {
            connection.TerminateConnection();
        }
        catch
        {
            // Keep the registration so the periodic revalidator can retry.
            return Task.CompletedTask;
        }
        connections.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }
}
