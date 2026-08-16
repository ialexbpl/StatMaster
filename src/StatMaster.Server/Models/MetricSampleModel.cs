namespace StatMaster.Server;

public sealed class MetricSampleModel
{
    public long Id { get; set; }

    public required string AgentId { get; set; }      // z Hello (np. AGENT-01)
    public required string MetricKey { get; set; }    // np. cpu.usage.percent
    public required string RawResponse { get; set; }  // np. OK|34.4 lub ERR|timeout

    public string? ValueText { get; set; }            // tylko część po OK|
    public string? ErrorText { get; set; }            // tylko część po ERR|
    public bool IsError { get; set; }                 // szybki filtr błędów

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}