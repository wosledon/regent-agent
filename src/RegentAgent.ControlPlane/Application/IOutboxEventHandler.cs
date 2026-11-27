using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Application;

public interface IOutboxEventHandler
{
    string EventType { get; }
    Task HandleAsync(OutboxRecord message, CancellationToken cancellationToken);
}
