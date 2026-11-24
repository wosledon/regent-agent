namespace RegentAgent.ControlPlane.Application;

public sealed class CollaborationOptions
{
    public int DefaultWipLimit { get; set; } = 3;
    public int MaxInterruptsPerHour { get; set; } = 5;
    public int MaxCoordinationPayloadChars { get; set; } = 65_536;
}
