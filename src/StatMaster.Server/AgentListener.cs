using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StatMaster.Protocol;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;

//ogolnie cancellation token jest opcjonalny w .net ale wymagany przez metody asynchroniczne zeby przerwac operacje jesli agent sie odłaczy i zwolnic watki i zasoby odrazu

namespace StatMaster.Server;

public sealed class AgentListener //class to listen for the agent and query the metric with certificate and expected token
{
    private readonly int _port; //port to listen on
    private readonly MetricQueryService _queryService; //query service to query the metric /?///// ask what exactly isdone here are we defining variables or poinitng to a pllace or what
    private readonly X509Certificate2 _serverCertificate; //certificate to use for the server
    private readonly string _expectedToken; //expected token to use for the server
    private readonly IDbContextFactory<StatMasterDbContext> _dbFactory;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _rebootRequests =
        new(StringComparer.OrdinalIgnoreCase);

    public bool RequestReboot(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId) || !_sessions.ContainsKey(agentId))
            return false;

        _rebootRequests[agentId] = 1;
        Console.WriteLine($"[Server] Windows reboot queued for {agentId}");
        return true;
    }

    public bool ConsumeRebootRequest(string agentId)
    {
        return !string.IsNullOrWhiteSpace(agentId) && _rebootRequests.TryRemove(agentId, out _);
    }

    //storing the dependencies like tools we are using
    public AgentListener(
        int port,
        MetricQueryService queryService,
        X509Certificate2 serverCertificate,
        string expectedToken,
        IDbContextFactory<StatMasterDbContext> dbFactory)
    {
        _port = port;
        _queryService = queryService;
        _serverCertificate = serverCertificate;
        _expectedToken = expectedToken;
        _dbFactory = dbFactory;
    }

    //accept loop: kazde polaczenie dostaje wlasny watek OS (thread-per-connection)
    public async Task ListenAndServeAsync(
        Func<string, Stream, CancellationToken, Task> sessionHandler,
        CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();

        Console.WriteLine($"[Server] Listening on 0.0.0.0:{_port}");
        Console.WriteLine("[Server] Waiting for agent connections...");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                string remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                Console.WriteLine($"[Server] Agent connected: {remote}");
                StartSessionThread(client, remote, sessionHandler, cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private void StartSessionThread(
        TcpClient client,
        string remote,
        Func<string, Stream, CancellationToken, Task> sessionHandler,
        CancellationToken cancellationToken)
    {
        var thread = new Thread(() =>
        {
            HandleSessionAsync(client, sessionHandler, cancellationToken).GetAwaiter().GetResult();
        })
        {
            IsBackground = true,
            Name = $"agent-session-{remote}"
        };

        Console.WriteLine($"[Server] Session thread started: {thread.Name} (id={thread.ManagedThreadId})");
        thread.Start();
    }

    private async Task HandleSessionAsync(
        TcpClient client,
        Func<string, Stream, CancellationToken, Task> sessionHandler,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                await using var netStream = client.GetStream();
                await using var sslStream = new SslStream(netStream, false, (_, _, _, _) => true);

                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _serverCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = false
                },
                cancellationToken);

                Console.WriteLine("[Server] TLS established.");

                ProtocolFrame helloFrame = await FrameCodec.ReceiveFrameAsync(sslStream, cancellationToken);
                if (helloFrame.Type != MessageType.Hello)
                {
                    throw new InvalidOperationException($"Expected Hello, got: {helloFrame.Type}");
                }

                string helloText = Encoding.UTF8.GetString(helloFrame.Payload);
                Console.WriteLine($"[Server] Hello received: {helloText}");
                string[] parts = helloText.Split('|');

                if (parts.Length != 2)
                {
                    throw new InvalidOperationException("Invalid Hello format. Expected: agentId|token");
                }

                string agentId = parts[0];
                string token = parts[1];
                if (!string.Equals(token, _expectedToken, StringComparison.Ordinal))
                {
                    throw new UnauthorizedAccessException("Hello token is invalid.");
                }

                string? remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString();
                await UpsertAgentIpAsync(agentId, remoteIp, cancellationToken);

                Console.WriteLine($"[Server] Hello accepted. agentId={agentId}");

                using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                ReplaceSession(agentId, sessionCts);

                try
                {
                    await sessionHandler(agentId, sslStream, sessionCts.Token);
                }
                finally
                {
                    RemoveSession(agentId, sessionCts);
                }
            }
            catch (OperationCanceledException)
            {
                // shutdown albo nowa sesja tego samego AgentId zastapila stara
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Session ended: {ex.Message}");
            }
        }
    }

    private void ReplaceSession(string agentId, CancellationTokenSource sessionCts)
    {
        if (_sessions.TryRemove(agentId, out var previous))
        {
            Console.WriteLine($"[Server] Replacing existing session for {agentId}");
            previous.Cancel();
        }

        _sessions[agentId] = sessionCts;
    }

    private void RemoveSession(string agentId, CancellationTokenSource sessionCts)
    {
        if (_sessions.TryGetValue(agentId, out var current) && ReferenceEquals(current, sessionCts))
            _sessions.TryRemove(agentId, out _);
    }

    private async Task UpsertAgentIpAsync(string agentId, string? ip, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(ip))
            return;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var target = await db.AgentTargets.FirstOrDefaultAsync(x => x.AgentId == agentId, cancellationToken);

        if (target is null)
        {
            db.AgentTargets.Add(new AgentTargetModel
            {
                AgentId = agentId,
                DisplayName = agentId,
                HostOrIp = ip,
                Enabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else if (!string.Equals(target.HostOrIp, ip, StringComparison.OrdinalIgnoreCase))
        {
            target.HostOrIp = ip;
            target.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}//Responsibility of file: accept connections and hand each TLS session to a dedicated OS thread.
