using System.Net;
using System.Net.Sockets;
using System.Text;
using StatMaster.Protocol;

int port = 50001;
string keyToAsk = args.Length > 0 ? args[0] : "system.hostname";

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

Console.WriteLine($"[Server] Listening on 0.0.0.0:{port}");
Console.WriteLine("[Server] Waiting for agent connection...");

using var client = await listener.AcceptTcpClientAsync();
Console.WriteLine($"[Server] Agent connected: {client.Client.RemoteEndPoint}");

using var stream = client.GetStream();

byte[] askPayload = Encoding.UTF8.GetBytes(keyToAsk);
await FrameCodec.SendFrameAsync(stream, MessageType.AskForMetric, askPayload);
Console.WriteLine($"[Server] AskForMetric sent: {keyToAsk}");

ProtocolFrame response = await FrameCodec.ReceiveFrameAsync(stream);
if (response.Type != MessageType.ResponseMetric)
{
    Console.WriteLine($"[Server] Unexpected frame type: {response.Type}");
    return;
}

string responseText = Encoding.UTF8.GetString(response.Payload);
Console.WriteLine($"[Server] ResponseMetric: {responseText}");

if (responseText.StartsWith("OK|", StringComparison.Ordinal))
{
    Console.WriteLine($"[Server] Metric value: {responseText["OK|".Length..]}");
}
else
{
    Console.WriteLine($"[Server] Metric error: {responseText}");
}