namespace StatMaster.Agent;

public sealed class ScriptMetricDefinition
{
    public required string Key { get; init; }
    public required string FileName { get; init; }
    public string Arguments { get; init; } = "";
    public int TimeoutMs { get; init; } = 5000;
    public int MaxOutputChars { get; init; } = 4000;
    public bool Enabled { get; init; } = true;
}
//model customowej metryki skryptowej