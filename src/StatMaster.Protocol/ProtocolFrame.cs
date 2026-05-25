namespace StatMaster.Protocol;

/// <summary>
/// Jedna ramka protokołu: typ wiadomości + surowy payload.
/// </summary>

public class ProtocolFrame
{
    public ProtocolFrame(MessageType type, byte[] payload)
    { 
        Type = type;
        Payload = payload ?? Array.Empty<byte>();
    }

    public MessageType Type { get; }
    public byte[] Payload { get; }
}