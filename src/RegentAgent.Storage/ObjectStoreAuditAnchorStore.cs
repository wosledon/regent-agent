using System.Text;
using System.Text.Json;
using RegentAgent.Contracts.Governance;

namespace RegentAgent.Storage;

public sealed class ObjectStoreAuditAnchorStore(IObjectStore objects) : IAuditAnchorStore
{
    private readonly IObjectStore objects = objects;

    public async Task StoreAsync(AuditAnchorRecord anchor, CancellationToken cancellationToken)
    {
        var key = Key(anchor.TenantId, anchor.ThroughSeq);
        using var stream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(anchor));
        await objects.PutAsync(new ObjectUploadRequest(key, stream, "application/json"), cancellationToken);
    }

    public async Task<AuditAnchorRecord?> GetAsync(Guid tenantId, long throughSeq,
        CancellationToken cancellationToken)
    {
        await using var stream = await objects.OpenReadAsync(Key(tenantId, throughSeq), cancellationToken);
        return stream is null ? null : JsonSerializer.Deserialize<AuditAnchorRecord>(await new StreamReader(stream).ReadToEndAsync(cancellationToken));
    }

    private static string Key(Guid tenantId, long throughSeq) =>
        $"tenants/{tenantId:N}/audit-anchors/{throughSeq:D20}.json";
}
