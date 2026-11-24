using System.Text.Json;
using RegentAgent.Contracts.Governance;

namespace RegentAgent.Storage;

public sealed class LocalFileAuditAnchorStore(string root) : IAuditAnchorStore
{
    private readonly string root = Path.GetFullPath(root);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task StoreAsync(AuditAnchorRecord anchor, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, $"{anchor.TenantId:N}.jsonl");
        await gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(anchor) + Environment.NewLine, cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async Task<AuditAnchorRecord?> GetAsync(Guid tenantId, long throughSeq,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, $"{tenantId:N}.jsonl");
        if (!File.Exists(path)) return null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
            {
                var anchor = JsonSerializer.Deserialize<AuditAnchorRecord>(line);
                if (anchor is not null && anchor.TenantId == tenantId && anchor.ThroughSeq == throughSeq)
                    return anchor;
            }
            return null;
        }
        finally { gate.Release(); }
    }
}
