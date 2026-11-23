using RegentAgent.Contracts.Primitives;

namespace RegentAgent.Contracts.Events;

public sealed record EventEnvelope<T>(
    Guid EventId,
    TenantId TenantId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId,
    Guid? CausationId,
    T Payload)
{
    public static EventEnvelope<T> Create(
        TenantId tenantId,
        string eventType,
        T payload,
        Guid? correlationId = null,
        Guid? causationId = null) => new(
        Guid.NewGuid(), tenantId, eventType, 1, DateTimeOffset.UtcNow,
        correlationId, causationId, payload);
}
