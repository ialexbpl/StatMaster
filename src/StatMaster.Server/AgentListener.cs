using System.Net;
using System.Net.Sockets;
using System.Text;
using StatMaster.Protocol;

namespace StatMaster.Server;

public sealed class AgentListener //class to listen for the agent and query the metric
{
    private readonly int _port; //port to listen on
    private readonly MetricQueryService _queryService; //query service to query the metric /?///// ask what exactly isdone here are we defining variables or poinitng to a pllace or what

//storing the dependencies like tools we are using
    public AgentListener(int port, MetricQueryService queryService)
    {
        _port = port;
        _queryService = queryService;
    }

//just using this method to listen for the agent and query the metric once for testing purposes
    public async Task<string> ListenAndQueryOnceAsync(string keyToAsk, CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();

        Console.WriteLine($"[Server] Listening on 0.0.0.0:{_port}");
        Console.WriteLine("[Server] Waiting for agent connection...");

        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        Console.WriteLine($"[Server] Agent connected: {client.Client.RemoteEndPoint}");

        using var stream = client.GetStream(); //get the stream from the client

        //receiving the hello frame from the agent
        ProtocolFrame helloFrame = await FrameCodec.ReceiveFrameAsync(stream, cancellationToken);
        if (helloFrame.Type != MessageType.Hello)
        {
            throw new InvalidOperationException($"Expected Hello, got: {helloFrame.Type}");
        }
        // parsing the hello payload: agentId|token
        string helloText = Encoding.UTF8.GetString(helloFrame.Payload);
        Console.WriteLine($"[Server] Hello received: {helloText}");
        string[] parts = helloText.Split('|');
        if (parts.Length != 2)
        {
            throw new InvalidOperationException("Invalid Hello format. Expected: agentId|token");
        }
        string agentId = parts[0];
        string token = parts[1];
        // temporary token validation for MVP
        if (!string.Equals(token, "dev-token", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Hello token is invalid.");
        }
        Console.WriteLine($"[Server] Hello accepted. agentId={agentId}");

        return await _queryService.QueryMetricAsync(stream, keyToAsk, cancellationToken); //query the metric using the query service
    }
}
//Responsibility of file: accept connection and provide stream for query flow.