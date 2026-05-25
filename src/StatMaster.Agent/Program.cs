using System.Net.Sockets;
using System.Text;
using StatMaster.Protocol;

string serverHost = args.Length > 0 ? args[0] : "127.0.0.1";
int serverPort = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 50001;

var collectors = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
{
    ["system.hostname"] = () => Environment.MachineName,
    ["cpu.count"] = () => Environment.ProcessorCount.ToString()
};

using var client = new TcpClient();
Console.WriteLine($"[Agent] Connecting to {serverHost}:{serverPort}...");
await client.ConnectAsync(serverHost, serverPort);
Console.WriteLine("[Agent] Connected.");

using var stream = client.GetStream();

while (true)
{
    ProtocolFrame request;

    try
    {
        request = await FrameCodec.ReceiveFrameAsync(stream);
    }
    catch (EndOfStreamException)
    {
        Console.WriteLine("[Agent] Server closed connection.");
        break;
    }

    if (request.Type != MessageType.AskForMetric)
    {
        var wrongTypePayload = Encoding.UTF8.GetBytes($"ERR|unexpected-type:{request.Type}");
        await FrameCodec.SendFrameAsync(stream, MessageType.ResponseMetric, wrongTypePayload);
        continue;
    }

    string key = Encoding.UTF8.GetString(request.Payload);
    Console.WriteLine($"[Agent] AskForMetric: {key}");

    string responseText;
    if (collectors.TryGetValue(key, out var collector))
    {
        try
        {
            string value = collector();
            responseText = $"OK|{value}";
        }
        catch (Exception ex)
        {
            responseText = $"ERR|collector-failed:{ex.Message}";
        }
    }
    else
    {
        responseText = "ERR|unknown-key";
    }

    byte[] responsePayload = Encoding.UTF8.GetBytes(responseText);
    await FrameCodec.SendFrameAsync(stream, MessageType.ResponseMetric, responsePayload);
    Console.WriteLine($"[Agent] ResponseMetric: {responseText}");
}