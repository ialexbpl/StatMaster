using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;

public sealed class MetricQueryTimeout
{
    public int TimeoutMsPerMetric { get; init; } = 5000;
    public int MaxRetries { get; init; } = 1;
    public int RetryDelayMs { get; init; } = 300;

    public static MetricQueryTimeout FromConfiguration(IConfiguration configuration)
    {
        return new MetricQueryTimeout
        {
            TimeoutMsPerMetric = configuration.GetValue("MetricQuery:TimeoutMsPerMetric", 5000),
            MaxRetries = configuration.GetValue("MetricQuery:MaxRetries", 1),
            RetryDelayMs = configuration.GetValue("MetricQuery:RetryDelayMs", 300)
        };
    }
}
