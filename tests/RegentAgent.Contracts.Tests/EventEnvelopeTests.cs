using RegentAgent.Contracts.Events;
using RegentAgent.Contracts.Primitives;
using Xunit;

namespace RegentAgent.Contracts.Tests;

public sealed class EventEnvelopeTests
{
    [Fact]
    public void Create_sets_identity_tenant_and_schema_version()
    {
        var tenantId = TenantId.New();
        var envelope = EventEnvelope<object>.Create(tenantId, "test.created", new { value = 1 });

        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.Equal(tenantId, envelope.TenantId);
        Assert.Equal("test.created", envelope.EventType);
        Assert.Equal(1, envelope.SchemaVersion);
    }
}
