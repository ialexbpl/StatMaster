# StatMaster Deployment Guide (Server + Agent + TLS)

This guide combines installation and TLS setup into one flow.
Use it for one server + one agent first, then repeat the Agent section for more PCs.

---

## 1) Prerequisites

- Windows machine(s)
- .NET SDK 8 installed
- Repo cloned locally
- Open server firewall port `50001` (Agent TCP/TLS)
- Optional: open web UI port if you expose UI remotely

---

## 2) Generate premade TLS cert pair in repo `certs` folder

Run PowerShell as Administrator on the server PC.
From repo root:

```powershell
cd C:\Users\spart\source\repos\StatMaster
```

Edit values first:

- `$dns` -> server DNS/hostname (recommended)
- `$ip` -> server LAN IP
- password string -> strong password

```powershell
$dns = "statmaster.local"
$ip  = "172.20.10.3"
$pfxPassword = ConvertTo-SecureString "ChangeThisStrongPassword!" -AsPlainText -Force

$cert = New-SelfSignedCertificate `
  -Subject "CN=$dns" `
  -FriendlyName "StatMaster Server TLS" `
  -CertStoreLocation "Cert:\LocalMachine\My" `
  -KeyExportPolicy Exportable `
  -KeyLength 2048 `
  -KeyAlgorithm RSA `
  -HashAlgorithm SHA256 `
  -NotAfter (Get-Date).AddYears(2) `
  -TextExtension @(
    "2.5.29.37={text}1.3.6.1.5.5.7.3.1",
    "2.5.29.17={text}DNS=$dns&IP Address=$ip"
  )

New-Item -ItemType Directory -Force ".\certs" | Out-Null
Export-PfxCertificate -Cert $cert -FilePath ".\certs\statmaster-server.pfx" -Password $pfxPassword
Export-Certificate   -Cert $cert -FilePath ".\certs\statmaster-server.cer"
```

Expected files:

- `certs\statmaster-server.pfx` (server only)
- `certs\statmaster-server.cer` (copy to agents)

---

## 3) Configure server (`StatMaster.Server`)

File: `src/StatMaster.Server/appsettings.json`

```json
"Server": {
  "Port": 50001,
  "CertPath": "../../certs/statmaster-server.pfx",
  "CertPassword": "ChangeThisStrongPassword!",
  "ExpectedToken": "dev-token"
}
```

Rules:

- `Port` must match Agent `ServerPort`
- `ExpectedToken` must match Agent `Token`

Start server from repo root:

```powershell
dotnet run --project .\src\StatMaster.Server
```

---

## 4) Trust server cert on agent PC

Copy this file from server to each agent PC:

- `certs\statmaster-server.cer`

On each agent PC (PowerShell as Administrator):

```powershell
Import-Certificate -FilePath "C:\Path\statmaster-server.cer" -CertStoreLocation "Cert:\LocalMachine\Root"
```

---

## 5) Configure agent (`StatMaster.Agent`)

File: `src/StatMaster.Agent/appsettings.json`

### Remote PC (recommended for deployment, IP-only)

```json
{
  "Agent": {
    "ServerHost": "172.20.10.3",
    "ServerPort": 50001,
    "AgentId": "AGENT-01",
    "Token": "CHANGE_ME_SHARED_TOKEN",
    "TlsTargetHost": "172.20.10.3",
    "ReconnectDelaySeconds": 5
  }
}
```

### Same PC (local test only)

```json
{
  "Agent": {
    "ServerHost": "127.0.0.1",
    "ServerPort": 50001,
    "AgentId": "AGENT-01",
    "Token": "CHANGE_ME_SHARED_TOKEN",
    "TlsTargetHost": "127.0.0.1",
    "ReconnectDelaySeconds": 5
  }
}
```

Rules:

- `ServerHost` = where agent connects (IP or DNS)
- `TlsTargetHost` = certificate name in SAN/CN (must match cert)
- `Token` = must match server `ExpectedToken`
- For IP-only setup, set `ServerHost` and `TlsTargetHost` to the same server IP.

Start agent:

```powershell
dotnet run --project .\src\StatMaster.Agent
```

---

## 6) First UI login

- Open: `http://127.0.0.1:50001` (local UI)
- Default: `admin / admin`
- First login forces password change

---

## 7) Verification checklist

1. Server console shows agent connection.
2. Agent console shows `TLS established`.
3. Metrics are exchanged (`AskForMetric` / `ResponseMetric`).
4. Dashboard shows agent and latest metrics.

---

## 8) Scale to more PCs

Repeat only agent steps on each additional PC:

- trust `statmaster-server.cer`
- set server host/port/token/tls target
- assign unique `AgentId` (`AGENT-02`, `AGENT-03`, ...)

Server config stays the same.

---

## 9) Common deployment mistakes

- Agent still uses `127.0.0.1` for remote server.
- `TlsTargetHost` does not match cert SAN/CN.
- Agent PC did not import cert to Trusted Root.
- `ServerPort` blocked by firewall.
- Token mismatch between server and agent.

