using System.Buffers.Binary;

namespace StatMaster.Protocol;

/// <summary>
/// Kodowanie/dekodowanie ramek:
/// [2 bajty Type][4 bajty PayloadLength][Payload].
/// </summary>
public static class FrameCodec
{
    public const int HeaderSize = 6;
    public const int MaxPayloadBytes = 1024 * 1024; // 1 MB na start

    public static async Task SendFrameAsync(
        Stream stream, //bytes to write to
        MessageType type, //message type or id like 4 for "PlayerInfo"
        byte[] payload, //actual message data, can be empty but not null
        CancellationToken cancellationToken = default)// allow to cancel the write operation
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        payload ??= Array.Empty<byte>(); //if payload is null its just empty but not null

        if (payload.Length > MaxPayloadBytes) // b4 we send we check size
        {
            throw new InvalidOperationException(
                $"Payload is too large: {payload.Length} bytes.");
        }

        byte[] header = new byte[HeaderSize]; //alocate header buffer 6 bytes

        // 0..1 => MessageType (ushort), little-endian
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), (ushort)type);

        // 2..5 => PayloadLength (uint), little-endian
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2, 4), (uint)payload.Length);

        await stream.WriteAsync(header, cancellationToken);//send header first
        if (payload.Length > 0) //then send payload if we have any
        {
            await stream.WriteAsync(payload, cancellationToken);
        }
    }

    public static async Task<ProtocolFrame> ReceiveFrameAsync(
        Stream stream,
        // wiadomosc po wykonaniu funkcji w agencie np  uzycie tego cpu
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        byte[] header = new byte[HeaderSize];
        await FillBufferAsync(stream, header, cancellationToken);

        ushort messageType = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0, 2));
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));

        if (payloadLength > MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Payload length exceeds limit: {payloadLength} bytes.");
        }

        byte[] payload = new byte[payloadLength];
        if (payloadLength > 0)
        {
            await FillBufferAsync(stream, payload, cancellationToken);
        }

        return new ProtocolFrame((MessageType)messageType, payload);
    }

    private static async Task FillBufferAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Stream closed before frame was fully read.");
            }

            offset += read;
        }
    }
}


///diagram przeplywu danyc done
/// funkcjonalny diagram
/// tcp lsitner
/// tcp client
/// hardcoded message between them
/// diagram komponentow systemu