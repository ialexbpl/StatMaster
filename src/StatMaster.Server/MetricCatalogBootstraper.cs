using Microsoft.Extensions.Configuration;

namespace StatMaster.Server;

public static class MetricCatalogBootstrapper
{
    public static void SeedFromAppsettingsIfEmpty(StatMasterDbContext db, IConfiguration configuration)
    {
        if (db.MetricDefinitions.Any())
            return;

        var items = MetricConfigReader.ResolveEnabledItems(configuration);

        var rows = items.Select(i => new MetricDefinitionModel
        {
            Key = i.Key,
            Description = i.Description,
            ValueType = i.ValueType,
            Unit = i.Unit,
            Enabled = i.Enabled,
            Source = i.Source,
            Target = i.Target,
            IntervalSeconds = i.IntervalSeconds
        });

        db.MetricDefinitions.AddRange(rows);
        db.SaveChanges();
    }
}