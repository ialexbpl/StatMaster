namespace StatMaster.Protocol;

/// <summary>
/// Jedna ramka protokołu: typ wiadomości + surowy payload.
/// </summary>

public class ProtocolFrame //frame model class
{
    public ProtocolFrame(MessageType type, byte[] payload) //constructor to store the type and payload
    { 
        Type = type; //assign the type
        Payload = payload ?? Array.Empty<byte>(); //assign the payload or empty array if null
    }

    public MessageType Type { get; } //read-only Type property
    public byte[] Payload { get; } //read-only Payload property
}
//Responsibility of file: in-memory model of one protocol frame.