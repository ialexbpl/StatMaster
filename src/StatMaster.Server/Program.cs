using StatMaster.Server;

int port = 50001;
string keyToAsk = args.Length > 0 ? args[0] : "system.hostname";

var queryService = new MetricQueryService();
var listener = new AgentListener(port, queryService);
string responseText = await listener.ListenAndQueryOnceAsync(keyToAsk);

Console.WriteLine($"[Server] ResponseMetric: {responseText}");

if (responseText.StartsWith("OK|", StringComparison.Ordinal))
{
    Console.WriteLine($"[Server] Metric value: {responseText["OK|".Length..]}");
}
else
{
    Console.WriteLine($"[Server] Metric error: {responseText}");
}