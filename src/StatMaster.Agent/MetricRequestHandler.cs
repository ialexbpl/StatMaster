using System.Text;
using StatMaster.Protocol;

namespace StatMaster.Agent;

public sealed class MetricRequestHandler//class to handle the request from the server
{
    private readonly MetricRegistry _metricRegistry; //dependency injection

    public MetricRequestHandler(MetricRegistry metricRegistry) //constructor to store the dependency
    {
        _metricRegistry = metricRegistry;
    }

//method to handle the request from the server
    public async Task HandleAsync(ProtocolFrame request, Stream stream, CancellationToken cancellationToken = default)
    {
        string responseText; //local variable to store the response

        if (request.Type != MessageType.AskForMetric) //if the request type is not AskForMetric the right type, return an error
        {
            responseText = $"ERR|unexpected-type:{request.Type}";
        }
        else
        {
            string key = Encoding.UTF8.GetString(request.Payload); //decode the key from the payload
            Console.WriteLine($"[Agent] AskForMetric: {key}"); //log the key
            responseText = _metricRegistry.Collect(key); //call the registry to collect the metric
        }

        byte[] responsePayload = Encoding.UTF8.GetBytes(responseText); //encode the response to bytes

        //send the response to the server again using my shared protocol
        await FrameCodec.SendFrameAsync(stream, MessageType.ResponseMetric, responsePayload, cancellationToken);
        Console.WriteLine($"[Agent] ResponseMetric: {responseText}"); //log the response
    }
}

//Responsibility of file: protocol-level handling of metric requests.