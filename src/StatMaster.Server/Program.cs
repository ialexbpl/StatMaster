using StatMaster.Server;

int port = 50001; //port and server config need to be configurable on both ends
string keyToAsk = args.Length > 0 ? args[0] : "system.hostname"; //keys also changable

var queryService = new MetricQueryService();
var listener = new AgentListener(port, queryService);
string responseText = await listener.ListenAndQueryOnceAsync(keyToAsk);

Console.WriteLine($"[Server] ResponseMetric: {responseText}");//jus a quick check if the response is OK or not

if (responseText.StartsWith("OK|", StringComparison.Ordinal))
{
    Console.WriteLine($"[Server] Metric value: {responseText["OK|".Length..]}");
}
else
{
    Console.WriteLine($"[Server] Metric error: {responseText}");
}
//Responsibility of file: server bootstrap and output formatting.