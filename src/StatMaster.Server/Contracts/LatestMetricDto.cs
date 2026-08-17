namespace StatMaster.Server.Contracts;

public sealed class LatestMetricDto
{
    public required string AgentId { get; init; }
    public required string MetricKey { get; init; }
    public required string RawResponse { get; init; }
    public string? ValueText { get; init; }
    public string? ErrorText { get; init; }
    public bool IsError { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}
