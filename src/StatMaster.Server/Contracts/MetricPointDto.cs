namespace StatMaster.Server.Contracts;

public sealed class MetricPointDto
{
    public DateTimeOffset CapturedAt { get; init; }
    public string? ValueText { get; init; }
    public bool IsError { get; init; }
    public string RawResponse { get; init; } = string.Empty;
}
