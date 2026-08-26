namespace StatMaster.Server.Contracts;

public sealed class AgentSummaryDto
{
    public required string AgentId { get; init; }
    public string? DisplayName { get; init; }
    public string? HostOrIp { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset? LastSeen { get; init; }
    public bool Online { get; init; }
}
