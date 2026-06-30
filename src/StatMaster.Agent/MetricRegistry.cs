namespace StatMaster.Agent;

//same sealing as in AgentClient
public sealed class MetricRegistry
{
    private readonly Dictionary<string, Func<string>> _collectors = //dictionary of collectors
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["system.hostname"] = () => Environment.MachineName, // testing to get the machine name
            ["cpu.count"] = () => Environment.ProcessorCount.ToString(), // testing to get the processor count
        };

    public string Collect(string key) //collect the metric
    {
        if (!_collectors.TryGetValue(key, out var collector))
        {
            return "ERR|unknown-key";
        }

        try
        {
            return $"OK|{collector()}"; //return the value from the collector
        }
        catch (Exception ex)
        {
            return $"ERR|collector-failed:{ex.Message}";
        }
    }
}

//Responsibility of file: key lookup and metric execution
