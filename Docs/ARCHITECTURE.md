# StatMaster — architektura (stan w kodzie)

Dokument opisuje **to, co jest zaimplementowane** w `src/`. Nie ma tu terminala zdalnego, SignalR do metryk ani testów xUnit.

**TL;DR:** Agent otwiera TCP do serwera, robi TLS (`SslStream`), wysyła `Hello` (`agentId|token`). Serwer na każde połączenie odpala osobny wątek OS. Na tym samym strumieniu serwer cyklicznie wysyła `AskForMetric(klucz)`, agent liczy wartość z rejestru (built-in albo skrypt) i odsyła `ResponseMetric` (`OK|...` / `ERR|...`). Katalog *co i jak często pytać* jest w SQLite; *jak wykonać klucz* jest u agenta.

```
[PC z agentem]                         [Serwer StatMaster]
 AgentClient  --TCP:50001-->  TcpListener (accept loop)
      |                              |
 SslStream (klient)            SslStream (serwer, PFX)
      |                              |
 Hello agentId|token           walidacja tokenu
      |                              |
 pętla ReceiveFrame            Thread agent-session-{ip}
      |                              |
 Collect(key)  <--- AskForMetric --- MetricScheduler
 ResponseMetric --->             zapis MetricSamples
```

Trzy projekty:

| Projekt | Rola |
|---------|------|
| `StatMaster.Protocol` | Wspólny format ramki i typy wiadomości |
| `StatMaster.Agent` | Łączy się wychodząco, wykonuje klucze |
| `StatMaster.Server` | TLS listener + scheduler + Blazor + SQLite |

---

## 1. Protokół ramek

Pliki: `src/StatMaster.Protocol/MessageType.cs`, `ProtocolFrame.cs`, `FrameCodec.cs`.

Na drucie każda wiadomość to **6 bajtów nagłówka + payload**:

```
[Type: 2 B, ushort LE] [Length: 4 B, uint LE] [Payload: Length bajtów]
```

Limit payloadu: `1 MB` (`FrameCodec.MaxPayloadBytes`). Odczyt jest „read exactly”: pętla `ReadAsync` aż bufor jest pełny; `Read == 0` = EOF.

Typy (`MessageType`):

| Wartość | Nazwa | Kierunek | Payload UTF-8 |
|---------|--------|----------|----------------|
| 1 | `Hello` | Agent → Server | `agentId\|token` |
| 2 | `AskForMetric` | Server → Agent | klucz, np. `cpu.count` |
| 3 | `ResponseMetric` | Agent → Server | `OK\|wartość` albo `ERR\|powód` |
| 4 | `Heartbeat` | Agent → Server | `agentId` (gdy 20 s ciszy) |

Wysyłanie:

```csharp
byte[] header = new byte[6];
BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0, 2), (ushort)type);
BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2, 4), (uint)payload.Length);
await stream.WriteAsync(header, cancellationToken);
if (payload.Length > 0)
    await stream.WriteAsync(payload, cancellationToken);
```

To samo `FrameCodec` używają obie strony. Nad TLS to zwykły `Stream` — codec nie wie, czy to `NetworkStream` czy `SslStream`.

---

## 2. Kto otwiera połączenie (outbound agent)

**Agent dzwoni do serwera.** Na stacji nie ma portu przychodzącego. NAT/firewall stacji nie blokuje tego modelu.

Konfiguracja agenta: `src/StatMaster.Agent/appsettings.json`.

| Pole | Znaczenie |
|------|-----------|
| `ServerHost` | Adres IP/DNS **do TCP** (dokąd łączyć socket) |
| `ServerPort` | Port listenera (domyślnie `50001`) |
| `TlsTargetHost` | Nazwa sprawdzana w certyfikacie (SAN/CN), **nie** musi być równa `ServerHost` |
| `AgentId` | Identyfikator stacji po Hello |
| `Token` | Shared secret, ten sam co `Server:ExpectedToken` |
| `ReconnectDelaySeconds` | Pauza po zerwaniu sesji |

`ServerHost` i `TlsTargetHost` to dwie różne rzeczy. TCP idzie na aktualny IP serwera. TLS sprawdza, czy certyfikat ma w SAN/CN wartość `TlsTargetHost`. Certyfikat jest wypalony w PFX (`Server:CertPath`), nie w JSON-ie.

---

## 3. SslStream — handshake krok po kroku

### 3.1 Agent (klient TLS)

`src/StatMaster.Agent/AgentClient.cs` → `RunSingleSessionAsync`:

```csharp
using var client = new TcpClient();
await client.ConnectAsync(_options.ServerHost, _options.ServerPort, cancellationToken);

using var netStream = client.GetStream();
using var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);

await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
{
    TargetHost = _options.TlsTargetHost ?? _options.ServerHost,
    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
}, cancellationToken);
```

Potem Hello i pętla odbioru. Domyślna walidacja certyfikatu .NET zostaje włączona: `TargetHost` musi pasować do SAN/CN.

### 3.2 Serwer (serwer TLS)

`src/StatMaster.Server/AgentListener.cs` → `HandleSessionAsync`:

```csharp
await using var netStream = client.GetStream();
await using var sslStream = new SslStream(netStream, false, (_, _, _, _) => true);

await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
{
    ServerCertificate = _serverCertificate,
    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    ClientCertificateRequired = false
}, cancellationToken);
```

Uwagi z kodu:

- **Brak mutual TLS.** Agent nie pokazuje certyfikatu klienta.
- Callback `(_, _, _, _) => true` po stronie serwera **nie weryfikuje certyfikatu agenta** (i tak go nie ma). To nie wyłącza sprawdzania certyfikatu serwera u agenta.
- Certyfikat serwera: PFX z `ServerRuntimeOptions.LoadCertificate()` (`CertPath` + `CertPassword` w `appsettings.json`).

Po TLS serwer **czeka na Hello**, nie na metrykę:

```csharp
ProtocolFrame helloFrame = await FrameCodec.ReceiveFrameAsync(sslStream, cancellationToken);
if (helloFrame.Type != MessageType.Hello)
    throw new InvalidOperationException($"Expected Hello, got: {helloFrame.Type}");

string[] parts = Encoding.UTF8.GetString(helloFrame.Payload).Split('|');
// parts[0] = agentId, parts[1] = token
if (!string.Equals(token, _expectedToken, StringComparison.Ordinal))
    throw new UnauthorizedAccessException("Hello token is invalid.");
```

Token jest shared secret **wewnątrz** już zaszyfrowanego kanału. To nie jest challenge-response. Cookie logowania do panelu (`/auth/login`) to osobna ścieżka — agenci jej nie używają.

Po akceptacji Hello serwer zapisuje IP stacji do `AgentTargets` (`UpsertAgentIpAsync`).

---

## 4. Thread-per-connection — gdzie i jak

**Gdzie:** `AgentListener.ListenAndServeAsync` + `StartSessionThread`.  
**Start:** `Program.cs` odpala listener w `Task.Run` obok Kestrelu (Blazor na osobnym porcie HTTP, np. `:5101`).

Accept loop (jeden wątek/task nasłuchu):

```csharp
using var listener = new TcpListener(IPAddress.Any, _port);
listener.Start();
while (!cancellationToken.IsCancellationRequested)
{
    TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
    StartSessionThread(client, remote, sessionHandler, cancellationToken);
}
```

Każde zaakceptowane gniazdo dostaje **osobny wątek OS**:

```csharp
var thread = new Thread(() =>
{
    HandleSessionAsync(client, sessionHandler, cancellationToken).GetAwaiter().GetResult();
})
{
    IsBackground = true,
    Name = $"agent-session-{remote}"
};
thread.Start();
```

`GetAwaiter().GetResult()` trzyma wątek przez całą sesję: TLS + Hello + scheduler (minuty/godziny). To jest celowe thread-per-connection, nie `async` accept bez wątku.

Po Hello `Program.cs` podpina handler:

```csharp
sessionHandler: (agentId, stream, cancellationToken) =>
{
    var sessionScheduler = new MetricScheduler(queryService, dbFactory, listener);
    return sessionScheduler.RunAsync(agentId, stream, itemsToAsk, cancellationToken);
}
```

Każda sesja ma **własny** `MetricScheduler`. SQLite jest współdzielony przez `IDbContextFactory` + `PRAGMA journal_mode=WAL` i `busy_timeout=5000`.

Słownik `_sessions` (`ConcurrentDictionary<string, CancellationTokenSource>`):

- klucz = `AgentId` (case-insensitive)
- ten sam `AgentId` drugi raz → `ReplaceSession` anuluje starą sesję
- `RequestReboot(agentId)` działa tylko gdy klucz jest w `_sessions` (agent online)

Dwa agenty = dwa różne `AgentId` w ich `appsettings.json`. Ten sam ID = jedna żywa sesja.

Na **jednym** strumieniu klucze idą **sekwencyjnie** (`QueryMetricsAsync` to pętla `foreach`). Nie ma multiplexu Asków na jednej sesji.

---

## 5. Pętla zapytań (scheduler)

`src/StatMaster.Server/MetricScheduler.cs`

Co ~10 s przeładowuje katalog z DB (`ReloadItems` → `DbMetricCatalogReader`). Grupuje włączone klucze po `IntervalSeconds`. Gdy minie interwał, woła:

```csharp
var responses = await _queryService.QueryMetricsAsync(stream, keys, cancellationToken);
await SaveSamplesAsync(agentId, responses, now, cancellationToken);
```

`MetricQueryService.QueryMetricAsync`:

1. `SendFrameAsync(stream, AskForMetric, UTF8(key))`
2. `ReceiveFrameAsync` — pomija `Heartbeat`
3. oczekuje `ResponseMetric`
4. timeout/retry z sekcji `MetricQuery` w `appsettings.json`

Zapis: tabela `MetricSamples` (`AgentId`, `MetricKey`, `RawResponse`, `ValueText` / `ErrorText`, `IsError`, `CapturedAtUtc`).

`system.reboot` **nie** wchodzi do grup polla (`BuildGroups` go wyklucza). Trafia na strumień tylko gdy UI kolejkuje reboot i `ConsumeRebootRequest` zwraca true.

---

## 6. Co robi agent po AskForMetric

`MetricRequestHandler.HandleAsync`:

```csharp
if (request.Type != MessageType.AskForMetric)
    responseText = $"ERR|unexpected-type:{request.Type}";
else
{
    string key = Encoding.UTF8.GetString(request.Payload);
    responseText = _metricRegistry.Collect(key);
}
await FrameCodec.SendFrameAsync(stream, MessageType.ResponseMetric, Encoding.UTF8.GetBytes(responseText), ...);
```

`MetricRegistry.Collect`:

```csharp
if (!_collectors.TryGetValue(key, out var collector))
    return "ERR|unknown-key";
try { return $"OK|{collector()}"; }
catch (Exception ex) { return $"ERR|collector-failed:{ex.Message}"; }
```

Nieznany klucz **nie zrywa sesji**. Serwer dostaje `ERR|unknown-key` i zapisuje próbkę z `IsError = true`.

Heartbeat: jeśli agent 20 s nie dostanie ramki, sam wysyła `Heartbeat` i wraca do `ReceiveFrameAsync`. Serwer przy Asku te ramki pomija.

---

## 7. Klucze built-in („z buta”)

Słownik w konstruktorze `MetricRegistry` — kod C#, kompilowany z agentem.

| Klucz | Skąd wartość |
|-------|----------------|
| `system.hostname` | `Environment.MachineName` |
| `cpu.count` | `Environment.ProcessorCount` |
| `cpu.usage.percent` | `GetSystemTimes` (delta idle/total) |
| `os.description` | `RuntimeInformation.OSDescription` |
| `ram.total.mb` / `ram.used.mb` | `GlobalMemoryStatusEx` |
| `disk.total.gb` / `disk.free.gb` | `DriveInfo` dysku systemowego |
| `system.uptime` | `Environment.TickCount64` |
| `system.reboot` | `shutdown.exe /r /t 5` — **nie pollować** |

Serwer wie, *że* ma pytać o te klucze, z katalogu (`MetricCatalog` → tabela `MetricDefinitions`). Agent wie, *jak* je policzyć, tylko z tego słownika.

Nowy built-in = zmiana **dwóch** miejsc: metoda w `MetricRegistry` + wpis w katalogu serwera (patrz §9).

---

## 8. Klucze custom (skrypty)

Przy starcie agenta `ScriptConfigLoader.Load()` czyta `agent-scripts.json` z `AppContext.BaseDirectory` (csproj kopiuje plik do output).

Każdy włączony wpis dokładany jest do tego samego `_collectors`:

```csharp
foreach (var def in ScriptConfigLoader.Load())
{
    _scriptDefinitions[def.Key] = def;
    _collectors[def.Key] = () => _scriptCollector.RunAsync(def).GetAwaiter().GetResult();
}
```

`ScriptCollector` odpala proces (`FileName` + `Arguments`), czeka max `TimeoutMs`, bierze stdout (limit `MaxOutputChars`). Exit code ≠ 0 albo timeout → wyjątek → `ERR|collector-failed:...`.

Przykład z `agent-scripts.json`:

```json
{
  "Key": "custom.top5.processes",
  "FileName": "powershell.exe",
  "Arguments": "-NoProfile -Command \"Get-Process | Sort-Object CPU -Descending | Select-Object -First 5 -ExpandProperty ProcessName\"",
  "TimeoutMs": 7000,
  "MaxOutputChars": 4000,
  "Enabled": true
}
```

**Podział odpowiedzialności:**

| Strona | Plik | Co ustala |
|--------|------|-----------|
| Agent | `agent-scripts.json` | Jak wykonać klucz (exe + argumenty) |
| Serwer | `MetricCatalog` / tabela `MetricDefinitions` | Czy pytać i co ile sekund |
| Panel | Agent Detail → Custom script schedule | Tylko `Enabled` + `IntervalSeconds` dla `Source == "script"` |

Panel **nie** edytuje skryptu i **nie** dodaje nowego klucza. UI zapisuje harmonogram POST-em `/api/metrics/custom-schedule`. Scheduler i tak odświeża DB co ~10 s.

Klucz musi istnieć **po obu stronach**. Sam wpis w katalogu serwera bez skryptu u agenta = `ERR|unknown-key`. Sam skrypt u agenta bez katalogu = agent umie liczyć, serwer nigdy nie zapyta.

`custom.system.logs` jest w `appsettings.json` serwera; w aktualnym `agent-scripts.json` go nie ma — dopóki nie dodasz skryptu na agencie, próbki będą błędami.

Interwał skryptów w UI **nie jest nadpisywany** z JSON przy restarcie serwera (`MetricCatalogBootstrapper` pomija `Enabled`/`IntervalSeconds` gdy `Source == "script"`). Built-in z JSON-a przy starcie mogą nadpisać te dwa pola w DB.

---

## 9. Gdzie admin wpisuje nowe rzeczy

### A. Nowa metryka skryptowa (najczęstsze)

1. **Agent** — `src/StatMaster.Agent/agent-scripts.json`  
   Nowy obiekt: unikalne `Key`, `FileName`, `Arguments`, timeout. Restart agenta (JSON ładuje się raz przy starcie).
2. **Serwer** — `src/StatMaster.Server/appsettings.json` → `MetricCatalog:Items`  
   Ten sam `Key`, `"Source": "script"`, `Enabled`, `IntervalSeconds`.
3. Restart serwera **albo** ręczny insert do `MetricDefinitions` (bootstrap dopisze brakujący klucz z JSON).
4. W panelu: Agent Detail → włącz / zmień interwał. To idzie do **wszystkich** agentów (katalog jest globalny, nie per-host).

### B. Nowa metryka wbudowana (C#)

1. `MetricRegistry` — wpis w słowniku + metoda kolektora.
2. `appsettings.json` serwera — `"Source": "built-in"`.
3. Rebuild + restart **agenta** (nowy kod) i serwera (katalog).
4. UI custom schedule tego nie pokaże (`Source != "script"`).

### C. Nowy agent / nowa stacja

Tylko `src/StatMaster.Agent/appsettings.json` na tej stacji:

- unikalny `AgentId`
- ten sam `Token` co `Server:ExpectedToken`
- `ServerHost` = osiągalny IP/DNS serwera
- `TlsTargetHost` = SAN/CN z certyfikatu (np. IP z SAN albo `statmaster.local`)

Po Hello wiersz w `AgentTargets` powstaje sam.

### D. Restart Windowsa na stacji

Nie katalog. Agent Detail → karta Restart Windows → wpisz `RESTART` → POST `/api/agents/{id}/restart` → `AgentListener.RequestReboot` → w pętli schedulera `AskForMetric(system.reboot)`. Agent musi mieć prawo odpalenia `shutdown.exe`.

### E. Nowy administrator panelu

`/profile` — Add administrator. Nie ma związku z tokenem agenta.

### F. Nowy typ ramki protokołu

`MessageType` + obsługa w `AgentClient` / `MetricRequestHandler` / `MetricQueryService`. Dziś używane są Hello, Ask, Response, Heartbeat.

### G. Czego nie ma w UI

- edycja skryptu PowerShell
- dodawanie klucza z przeglądarki
- zmiana tokenu / portu TLS (zostaje w `appsettings.json`)
- interwał per agent (jeden katalog na wszystkich)

---

## 10. Warstwa UI (osobny kanał)

Kestrel + Blazor Server + cookies. To **nie** jest kanał agenta.

- HTTP: logowanie, dashboard, wykresy z `MetricSamples`
- TCP `:50001`: wyłącznie protokół ramek nad TLS
- `MapBlazorHub()` = wewnętrzny SignalR Blazora, nie live-push metryk

Dashboard czyta SQLite przy renderze strony.

---

## 11. Pliki „gdzie szukać”

| Temat | Plik |
|--------|------|
| Format ramki | `src/StatMaster.Protocol/FrameCodec.cs` |
| Typy wiadomości | `src/StatMaster.Protocol/MessageType.cs` |
| TCP + TLS agenta | `src/StatMaster.Agent/AgentClient.cs` |
| Dispatch Ask → Collect | `src/StatMaster.Agent/MetricRequestHandler.cs` |
| Built-in + rejestr skryptów | `src/StatMaster.Agent/MetricRegistry.cs` |
| Definicje skryptów | `src/StatMaster.Agent/agent-scripts.json` |
| Accept + Thread + Hello | `src/StatMaster.Server/AgentListener.cs` |
| Start listenera | `src/StatMaster.Server/Program.cs` |
| Poll + zapis próbek | `src/StatMaster.Server/MetricScheduler.cs` |
| Ask/Response + timeout | `src/StatMaster.Server/MetricQueryService.cs` |
| Seed katalogu | `src/StatMaster.Server/MetricCatalogBootstraper.cs` |
| Katalog JSON | `src/StatMaster.Server/appsettings.json` (`MetricCatalog`) |
| Harmonogram z UI | `DashboardEndpoints` `/metrics/custom-schedule` |
| Cert/token/port | `ServerRuntimeOptions` + `appsettings.json` `Server` |
