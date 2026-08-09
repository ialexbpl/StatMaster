namespace StatMaster.Server;
public sealed class MetricModel
{
    public required string Key { get; init; }          // np. system.hostname
    public required string Description { get; init; }  // opis dla admina/UI
    public required string ValueType { get; init; }    // string/number/bool
    public required string Unit { get; init; }         // "", "count", "MB", "%"
    public bool Enabled { get; init; } = true;         // czy serwer ma odpytac
    public required string Source { get; init; }       // built-in / script
    public string Target { get; init; } = "all";       // all / grupa / host marker
    public int IntervalSeconds { get; init; } = 30;    // odstep odpytywania
}

    //responsibilities of file: define the model for the metric