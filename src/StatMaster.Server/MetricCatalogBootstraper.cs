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
            row.Enabled = item.Enabled;
            row.Source = item.Source;
            row.Target = item.Target;
            row.IntervalSeconds = item.IntervalSeconds;
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        db.SaveChanges();
    }
}