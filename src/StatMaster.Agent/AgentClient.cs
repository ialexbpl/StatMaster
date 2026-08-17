using System.Net.Sockets;
using System.Text;
using StatMaster.Protocol;
using System.Net.Security;
using System.Security.Authentication;

namespace StatMaster.Agent;

//sealing agent class so it can't be inherited and modified
public sealed class AgentClient
{
    private readonly AgentRuntimeOptions _options;
    private readonly MetricRequestHandler _requestHandler; //request handler dependency
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

//constructor to store dependencies
    public AgentClient(AgentRuntimeOptions options, MetricRequestHandler requestHandler)
    {
        _options = options;
        _requestHandler = requestHandler; //this checks the key from the server and returns the value from the registry
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunSingleSessionAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Agent] Connection/session error: {ex.Message}");
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            Console.WriteLine($"[Agent] Reconnecting in {_options.ReconnectDelaySeconds}s...");
            await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), cancellationToken);
        }
    }

    private async Task RunSingleSessionAsync(CancellationToken cancellationToken)
    {
        //really creating a tcp client and connect to server
        using var client = new TcpClient();
        Console.WriteLine($"[Agent] Connecting to {_options.ServerHost}:{_options.ServerPort}...");
        await client.ConnectAsync(_options.ServerHost, _options.ServerPort, cancellationToken); //wait for connection (stream)
        Console.WriteLine("[Agent] Connected.");

        using var netStream = client.GetStream(); //using the stream from the socket
        using var sslStream = new SslStream(netStream, leaveInnerStreamOpen: false);

        await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = _options.TlsTargetHost ?? _options.ServerHost,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
        }, cancellationToken);

        Console.WriteLine("[Agent] TLS established.");
        
        //sending the hello frame to the server
        // HELLO: agentId|token
        string helloText = $"{_options.AgentId}|{_options.Token}";
        byte[] helloPayload = Encoding.UTF8.GetBytes(helloText);

        await FrameCodec.SendFrameAsync(sslStream, MessageType.Hello, helloPayload, cancellationToken);
        Console.WriteLine($"[Agent] Hello sent: {helloText}");

        while (!cancellationToken.IsCancellationRequested) //receive loop until cancellation is requested
        {
            ProtocolFrame request; //declare the request frame from shared protocol
            try
            {
                using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                receiveCts.CancelAfter(HeartbeatInterval);
                request = await FrameCodec.ReceiveFrameAsync(sslStream, receiveCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                byte[] heartbeatPayload = Encoding.UTF8.GetBytes(_options.AgentId);
                await FrameCodec.SendFrameAsync(sslStream, MessageType.Heartbeat, heartbeatPayload, cancellationToken);
                Console.WriteLine("[Agent] Heartbeat sent.");
                continue;
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("[Agent] Server closed connection.");
                break;
            }

            await _requestHandler.HandleAsync(request, sslStream, cancellationToken); //delegate the request to the request handler
        }
    }
}
// Responsibility of file: establish agent-> server connection, keep receiving protocol frames in a loop,
// and dispatch each received frame to MetricRequestHandler for processing and response.