using System.Buffers.Binary;

namespace StatMaster.Protocol;

/// <summary>
/// Koduje i dekoduje ramki protokolu.
/// Format ramki na "drucie":
/// [2 bajty Type][4 bajty PayloadLength][Payload...].
/// </summary>
public static class FrameCodec
{
    // Rozmiar naglowka: 2 bajty typu + 4 bajty dlugosci payloadu.
    public const int HeaderSize = 6;

    // Bezpieczny limit payloadu (1 MB), zeby nie czytac/nie wysylac absurdalnie duzych danych.
    public const int MaxPayloadBytes = 1024 * 1024; // 1 MB na start

    public static async Task SendFrameAsync(
        Stream stream,                // Strumien wyjsciowy (NetworkStream/SslStream).
        MessageType type,             // Typ wiadomosci zapisywany do naglowka.
        byte[] payload,               // Dane wiadomosci (moga byc puste).
        CancellationToken cancellationToken = default) // Pozwala przerwac operacje I/O.
    {
        // Obrona przed null-em przekazanym zamiast strumienia.
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        // Jesli caller podal null, traktujemy to jako pusty payload.
        payload ??= Array.Empty<byte>();

        // Walidacja limitu, zanim zaczniemy wysylac cokolwiek.
        if (payload.Length > MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Payload is too large: {payload.Length} bytes.");
        }

        // Tworzymy bufor naglowka o stalej dlugosci 6 bajtow.
        byte[] header = new byte[HeaderSize];

        // Bajty 0..1: zapis typu wiadomosci jako ushort (little-endian).
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), (ushort)type);

        // Bajty 2..5: zapis dlugosci payloadu jako uint (little-endian).
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2, 4), (uint)payload.Length);

        // Najpierw zawsze wysylamy naglowek.
        await stream.WriteAsync(header, cancellationToken);

        // Potem wysylamy payload tylko wtedy, gdy ma jakas zawartosc.
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, cancellationToken);
        }
    }

    public static async Task<ProtocolFrame> ReceiveFrameAsync(
        Stream stream,                                // Strumien wejsciowy.
        CancellationToken cancellationToken = default) // Pozwala przerwac oczekiwanie na dane.
    {
        // Obrona przed null-em przekazanym zamiast strumienia.
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        // Bufor na naglowek (dokladnie 6 bajtow).
        byte[] header = new byte[HeaderSize];

        // Czytamy dokladnie 6 bajtow naglowka.
        await FillBufferAsync(stream, header, cancellationToken);

        // Odczyt typu wiadomosci z bajtow 0..1.
        ushort messageType = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0, 2));

        // Odczyt dlugosci payloadu z bajtow 2..5.
        uint payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));

        // Walidacja limitu po stronie odbiorcy (ochrona przed zlym/atakujacym nadawca).
        if (payloadLength > MaxPayloadBytes)
        {
            throw new InvalidOperationException(
                $"Payload length exceeds limit: {payloadLength} bytes.");
        }

        // Tworzymy bufor dokladnie na tyle bajtow, ile zapowiada naglowek.
        byte[] payload = new byte[payloadLength];

        // Jesli payload nie jest pusty, dociagamy wszystkie bajty.
        if (payloadLength > 0)
        {
            await FillBufferAsync(stream, payload, cancellationToken);
        }

        // Zamieniamy surowy typ (ushort) na enum i zwracamy gotowa ramke.
        return new ProtocolFrame((MessageType)messageType, payload);
    }

    private static async Task FillBufferAsync(
        Stream stream,                 // Zrodlo danych.
        byte[] buffer,                 // Bufor, ktory musi byc wypelniony w 100%.
        CancellationToken cancellationToken) // Przerwanie operacji.
    {
        // Offset = ile bajtow mamy juz zaladowanych do bufora.
        int offset = 0;

        // Petla dziala dopoki nie wypelnimy calego bufora.
        while (offset < buffer.Length)
        {
            // Czytamy "reszte" brakujacego fragmentu.
            int read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            // ReadAsync == 0 oznacza zamkniety strumien (EOF) przed koncem ramki.
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "Stream closed before frame was fully read.");
            }

            // Przesuwamy offset o liczbe faktycznie odczytanych bajtow.
            offset += read;
        }
    }
}