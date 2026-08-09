using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;

public static class MetricCatalogResolver
{
    public static string[] ResolveEnabledKeys(IConfiguration configuration)
    {
        string activeProfile = configuration["MetricCatalog:ActiveProfile"] ?? "dev";
        string targetScope = configuration["MetricCatalog:TargetScope"] ?? "all";

        var items = configuration
            .GetSection($"MetricCatalog:Profiles:{activeProfile}")
            .Get<List<MetricModel>>() ?? new List<MetricModel>();

        return items
            .Where(i => i.Enabled)
            .Where(i => string.Equals(i.Target, "all", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.Target, targetScope, StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
