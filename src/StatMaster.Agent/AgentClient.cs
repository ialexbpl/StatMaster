using System.Net.Sockets;
using StatMaster.Protocol;

namespace StatMaster.Agent;

//sealing agent class so it can't be inherited and modified
public sealed class AgentClient
{
    private readonly string _serverHost; //server host dependency
    private readonly int _serverPort; //server port dependency
    private readonly MetricRequestHandler _requestHandler; //request handler dependency

//constructor to store dependencies
    public AgentClient(string serverHost, int serverPort, MetricRequestHandler requestHandler)
    {
        _serverHost = serverHost;
        _serverPort = serverPort;
        _requestHandler = requestHandler; //this checks the key from the server and returns the value from the registry
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        //really creating a tcp client and connect to server
        using var client = new TcpClient();
        Console.WriteLine($"[Agent] Connecting to {_serverHost}:{_serverPort}...");
        await client.ConnectAsync(_serverHost, _serverPort); //wait for connection (stream)
        Console.WriteLine("[Agent] Connected.");

        using var stream = client.GetStream(); //using the stream from the socket
        while (!cancellationToken.IsCancellationRequested) //receive loop until cancellation is requested
        {
            ProtocolFrame request; //declare the request frame from shared protocol
            try
            {
                request = await FrameCodec.ReceiveFrameAsync(stream, cancellationToken);
            }
            catch (EndOfStreamException)
            {
                Console.WriteLine("[Agent] Server closed connection.");
                break;
            }

            await _requestHandler.HandleAsync(request, stream, cancellationToken); //delegate the request to the request handler
        }
    }
}
//Responsibility of file: connect + receive loop + dispatch.