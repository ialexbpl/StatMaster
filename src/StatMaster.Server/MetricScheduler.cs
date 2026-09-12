using Microsoft.EntityFrameworkCore;

namespace StatMaster.Server;


//klasa dla harmonogramu zap to agenta ktory bedzie odpytywac metryki w okreslonych odstepach czasu
public sealed class MetricScheduler
{
    public const string RebootKey = "system.reboot";

    private readonly MetricQueryService _queryService; //klasa typu MetricQueryService
    private readonly IDbContextFactory<StatMasterDbContext> _dbFactory;
    private readonly AgentListener _listener;

    public MetricScheduler(
        MetricQueryService queryService,
        IDbContextFactory<StatMasterDbContext> dbFactory,
        AgentListener listener)
    {
        _queryService = queryService;
        _dbFactory = dbFactory;
        _listener = listener;
    }

    public async Task RunAsync(
        string agentId,
        Stream stream,
        IReadOnlyList<MetricModel> items,
        CancellationToken cancellationToken = default)
    {
        var groups = BuildGroups(items);
        var nextRun = groups.Keys.ToDictionary(k => k, _ => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)));
        var nextConfigRefreshAt = DateTimeOffset.MinValue;

        while (!cancellationToken.IsCancellationRequested) //dopoki sesja nie jest anulowana
        {
            DateTimeOffset now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)); //pobieramy aktualny czas CEST
            if (now >= nextConfigRefreshAt)
            {
                var refreshed = ReloadItems(items);
                var rebuilt = BuildGroups(refreshed);
                if (rebuilt.Count > 0)
                {
                    foreach (int interval in rebuilt.Keys)
                    {
                        if (!nextRun.ContainsKey(interval))
                            nextRun[interval] = now;
                    }

                    foreach (int stale in nextRun.Keys.Except(rebuilt.Keys).ToList())
                        nextRun.Remove(stale);

                    groups = rebuilt;
                }

                nextConfigRefreshAt = now.AddSeconds(10);
            }
            bool anyExecuted = false; //czy wykonalismy jakies zapytania flaga czy wykonalismy jakies zapytania 

            if (_listener.ConsumeRebootRequest(agentId))
            {
                string rebootRaw = await _queryService.QueryMetricAsync(stream, RebootKey, cancellationToken);
                Console.WriteLine($"[Server] Reboot response ({agentId}): {rebootRaw}");
                anyExecuted = true;
            }

            foreach (var group in groups) //pętla po grupach metryk
            {
                int intervalSeconds = group.Key; //pobieramy odstep czasu
                string[] keys = group.Value; //pobieramy tablice kluczy metryk

                if (now < nextRun[intervalSeconds]) //jesli aktualny czas jest mniejszy niz czas kiedy nastepny raz bedzie odpytywac metryki
                    continue; //to przejdz do nastepnej grupy

                var responses = await _queryService.QueryMetricsAsync(stream, keys, cancellationToken); //odpytujemy metryki wysyla batch kluczy do queryService
                MetricResponsePrinter.Print(responses); //wypisujemy odpowiedzi

                await SaveSamplesAsync(agentId, responses, now, cancellationToken); //zapisujemy probki metryk do SQLite (czas CEST)

                nextRun[intervalSeconds] = now.AddSeconds(intervalSeconds); //zapisujemy czas kiedy nastepny raz bedzie odpytywac metryki
                anyExecuted = true;
            }

            if (!anyExecuted)
                await Task.Delay(250, cancellationToken); //krotka pauza 250ms dla cpu
        }
    }

    //metoda do zapisywania próbek metryk do bazy danych
     private async Task SaveSamplesAsync(
        string agentId,
        IReadOnlyDictionary<string, string> responses,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var samples = new List<MetricSampleModel>(responses.Count);
        foreach (var kv in responses)
        {
            string key = kv.Key;
            string raw = kv.Value ?? string.Empty;
            (bool isError, string? valueText, string? errorText) = ParseRawResponse(raw);
            samples.Add(new MetricSampleModel
            {
                AgentId = agentId,
                MetricKey = key,
                RawResponse = raw,
                ValueText = valueText,
                ErrorText = errorText,
                IsError = isError,
                CapturedAtUtc = capturedAtUtc
            });
        }
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.MetricSamples.AddRange(samples);
        await db.SaveChangesAsync(cancellationToken); //zapisujemy próbki metryk do bazy danych
    }
    private static (bool IsError, string? ValueText, string? ErrorText) ParseRawResponse(string raw)
    {
        if (raw.StartsWith("OK|", StringComparison.OrdinalIgnoreCase))
            return (false, raw.Length > 3 ? raw[3..] : string.Empty, null);
        if (raw.StartsWith("ERR|", StringComparison.OrdinalIgnoreCase))
            return (true, null, raw.Length > 4 ? raw[4..] : string.Empty);
        // fallback: traktuj jako value (stare/niestandardowe odpowiedzi)
        return (false, raw, null);
    }

    private static Dictionary<int, string[]> BuildGroups(IReadOnlyList<MetricModel> source)
    {
        return source
            .Where(x => x.Enabled && !string.Equals(x.Key, RebootKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(i => Math.Max(1, i.IntervalSeconds))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private List<MetricModel> ReloadItems(IReadOnlyList<MetricModel> fallbackItems)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var fromDb = DbMetricCatalogReader.ResolveEnabledItems(db);
            return fromDb.Count == 0 ? fallbackItems.ToList() : fromDb;
        }
        catch
        {
            return fallbackItems.ToList();
        }
    }
}
    


