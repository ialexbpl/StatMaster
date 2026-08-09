namespace StatMaster.Server;

public static class MetricResponsePrinter
{
    public static void Print(IReadOnlyDictionary<string, string> responses)
    {
        foreach (var item in responses)
        {
            string timestamp = DateTimeOffset.UtcNow.ToString("O");
            string key = item.Key;
            string responseText = item.Value;

            Console.WriteLine($"[{timestamp}] [Server] ResponseMetric for '{key}': {responseText}");

            if (responseText.StartsWith("OK|", StringComparison.Ordinal))
            {
                Console.WriteLine($"[{timestamp}] [Server] Metric value ({key}): {responseText["OK|".Length..]}");
            }
            else
            {
                Console.WriteLine($"[{timestamp}] [Server] Metric error ({key}): {responseText}");
            }
        }
    }
}

//responsibility of file: print the metric responses to the console 