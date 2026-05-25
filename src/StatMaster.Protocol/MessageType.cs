namespace StatMaster.Protocol;

/// <summary>
/// Rodzaj ramki w kanale agent ↔ serwer (nad TCP/TLS).
/// Taki słownik pozwala na łatwe dodawanie nowych typów wiadomości w przyszłości.
/// Wartość trafia na wire jako ushort (2 bajty, little-endian) w nagłówku.
/// </summary>
public enum MessageType : ushort ///wskazujemy, że typ wiadomości zajmuje 2 bajty
{
    /// <summary>Pierwsza wiadomość po połączeniu (np. id agenta, token, wersja protokołu).</summary>
    Hello = 1,

    /// <summary>Serwer prosi agenta o metrykę po „kluczu” (np. tekst w payloadzie UTF-8).</summary>
    AskForMetric = 2,  

    /// <summary>Agent odsyła wynik (np. wartość lub błąd jako UTF-8).</summary>
    ResponseMetric = 3,

    /// <summary>Utrzymanie sesji / brak danych — opcjonalnie na później.</summary>
    Heartbeat = 4,
}