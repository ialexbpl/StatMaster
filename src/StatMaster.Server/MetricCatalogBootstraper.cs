using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;

public static class MetricCatalogBootstrapper
{
    public static void SeedFromAppsettingsIfEmpty(StatMasterDbContext db, IConfiguration configuration)
    {
        var items = MetricConfigReader.ResolveEnabledItems(configuration);
        var existingByKey = db.MetricDefinitions
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (!existingByKey.TryGetValue(item.Key, out var row))
            {
                db.MetricDefinitions.Add(new MetricDefinitionModel
                {
                    Key = item.Key,
                    Description = item.Description,
                    ValueType = item.ValueType,
                    Unit = item.Unit,
                    Enabled = item.Enabled,
                    Source = item.Source,
                    Target = item.Target,
                    IntervalSeconds = item.IntervalSeconds,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                continue;
            }

            row.Description = item.Description;
            row.ValueType = item.ValueType;
            row.Unit = item.Unit;
            row.Source = item.Source;
            row.Target = item.Target;
            // Script schedule is owned by DB/UI. Do not reset Enabled/IntervalSeconds from JSON.
            if (!string.Equals(item.Source, "script", StringComparison.OrdinalIgnoreCase))
            {
                row.Enabled = item.Enabled;
                row.IntervalSeconds = item.IntervalSeconds;
            }
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        // OS is built-in (os.description). Do not keep a leftover custom.os.version schedule.
        var leftoverOsCustom = db.MetricDefinitions
            .Where(x => x.Key == "custom.os.version")
            .ToList();
        if (leftoverOsCustom.Count > 0)
            db.MetricDefinitions.RemoveRange(leftoverOsCustom);

        db.SaveChanges();
    }
}