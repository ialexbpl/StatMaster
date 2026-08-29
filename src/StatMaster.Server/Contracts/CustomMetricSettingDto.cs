namespace StatMaster.Server.Contracts;

public sealed class CustomMetricSettingDto
{
    public required string Key { get; set; }
    public required string Description { get; set; }
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; }
}
