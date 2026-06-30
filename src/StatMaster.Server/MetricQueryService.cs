using System.Text;
using StatMaster.Protocol;

namespace StatMaster.Server;

public sealed class MetricQueryService
{
    //method to query the metric for the given key
    //metric means the data we are collecting and storing in the registry
    public async Task<string> QueryMetricAsync(Stream stream, string key, CancellationToken cancellationToken = default)
    {
        byte[] askPayload = Encoding.UTF8.GetBytes(key); //encode the key to bytes
        await FrameCodec.SendFrameAsync(stream, MessageType.AskForMetric, askPayload, cancellationToken);//send the ask for metric frame to the agent
        Console.WriteLine($"[Server] AskForMetric sent: {key}");//log the key

        ProtocolFrame response = await FrameCodec.ReceiveFrameAsync(stream, cancellationToken);//receive the response from the agent
        if (response.Type != MessageType.ResponseMetric)
        {
            throw new InvalidOperationException($"Unexpected frame type: {response.Type}");
        }

        return Encoding.UTF8.GetString(response.Payload);
    }
}
//Responsibility of file: request/response protocol exchange.