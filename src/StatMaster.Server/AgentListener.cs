using System.Net;
using System.Net.Sockets;
using System.Text;
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


    //storing the dependencies like tools we are using
    public AgentListener(int port, MetricQueryService queryService, X509Certificate2 serverCertificate, string expectedToken)
    {
        _port = port;
        _queryService = queryService;
        _serverCertificate = serverCertificate;
        _expectedToken = expectedToken;
    }

    //listens and queries many keys in one TLS session
    public async Task ListenAndServeAsync(
        Func<string, Stream, CancellationToken, Task> sessionHandler,
        CancellationToken cancellationToken = default)
    {
        //listening for the agent on the port
        using var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();

        //LOGGING
        Console.WriteLine($"[Server] Listening on 0.0.0.0:{_port}");
        Console.WriteLine("[Server] Waiting for agent connection...");

        using var client = await listener.AcceptTcpClientAsync(cancellationToken); //akceptujemy polaczenie z agenta ale cancellation token jest opcjonalny 
        Console.WriteLine($"[Server] Agent connected: {client.Client.RemoteEndPoint}");

        using var netStream = client.GetStream();
        using var sslStream = new SslStream(netStream, false, (_, _, _, _) => true);

        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        { ServerCertificate = _serverCertificate, 
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, 
        ClientCertificateRequired = false }, 
        cancellationToken); //cancellationToken jest opcjonalny ale wymagany przez metode AuthenticateAsServerAsync zeby przerwac operacje jesli agent sie odłaczy

        Console.WriteLine("[Server] TLS established.");
    
        ProtocolFrame helloFrame = await FrameCodec.ReceiveFrameAsync(sslStream, cancellationToken); //receive the hello frame from the agent
        if (helloFrame.Type != MessageType.Hello)
        {
            throw new InvalidOperationException($"Expected Hello, got: {helloFrame.Type}");//warunek jesli agent nie wyslal hello frame albo wyslal nieprawidlowy frame
        }

        string helloText = Encoding.UTF8.GetString(helloFrame.Payload); //decodujemy zawartosc payload do stringa
        Console.WriteLine($"[Server] Hello received: {helloText}");
        string[] parts = helloText.Split('|'); //dzielimy stringa na dwie czesci po | np. agentId|token tylko dla odczytu

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

        Console.WriteLine($"[Server] Hello accepted. agentId={agentId}");

        await sessionHandler(agentId, sslStream, cancellationToken); //wywolujemy sessionHandler z agentId, sslStream i cancellationToken
    }
}//Responsibility of file: accept connection and provide stream for query flow.
