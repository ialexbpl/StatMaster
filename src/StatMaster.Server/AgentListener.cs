using System.Net;
using System.Net.Sockets;

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
        return await _queryService.QueryMetricAsync(stream, keyToAsk, cancellationToken); //query the metric using the query service
    }
}
//Responsibility of file: accept connection and provide stream for query flow.