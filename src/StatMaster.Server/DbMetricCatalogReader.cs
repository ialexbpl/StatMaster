namespace StatMaster.Server;


//czyta aktywne metryki z DB i mapuje je na MetricModel.
public static class DbMetricCatalogReader
{
    public static List<MetricModel> ResolveEnabledItems(StatMasterDbContext db)
    {
        var rows = db.MetricDefinitions
            .Where(x => x.Enabled)
            .OrderBy(x => x.Key)
            .ToList();

        return rows
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.UpdatedAtUtc).First())
            .OrderBy(x => x.Key)
            .Select(x => new MetricModel
            {
                Key = x.Key,
                Description = x.Description,
                ValueType = x.ValueType,
                Unit = x.Unit,
                Enabled = x.Enabled,
                Source = x.Source,
                Target = x.Target,
                IntervalSeconds = x.IntervalSeconds
            })
            .ToList();
    }
}