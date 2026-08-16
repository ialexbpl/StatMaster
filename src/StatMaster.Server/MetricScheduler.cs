namespace StatMaster.Server;


//klasa dla harmonogramu zap to agenta ktory bedzie odpytywac metryki w okreslonych odstepach czasu
public sealed class MetricScheduler
{
    private readonly MetricQueryService _queryService; //klasa typu MetricQueryService
    private readonly StatMasterDbContext _db;

    public MetricScheduler(MetricQueryService queryService, StatMasterDbContext db) //wstrzykniecie zaleznosci
    {
        _queryService = queryService;//przypisanie zaleznosci
        _db = db;//przypisanie zaleznosci
    }

    public async Task RunAsync(
        string agentId,
        Stream stream,
        IReadOnlyList<MetricModel> items,
        CancellationToken cancellationToken = default)
    {
        var groups = items //grupujemy metryki po odstepach czasu i tworzymy dictionary gdzie kluczem jest odstep czasu a wartoscia jest tablica kluczy metryk
            .GroupBy(i => Math.Max(1, i.IntervalSeconds))//grupujemy metryki po odstepach czasu i ochrzaniamy je na minimum 1 sekunde
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToArray());//tu robie dictionary gdzie kluczem jest odstep czasu a wartoscia jest tablica kluczy metryk

        var nextRun = groups.Keys.ToDictionary(k => k, _ => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2))); //nextrun zapisuje czas kiedy nastepny raz bedzie odpytywac metryki 

        while (!cancellationToken.IsCancellationRequested) //dopoki sesja nie jest anulowana
        {
            DateTimeOffset now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(2)); //pobieramy aktualny czas CEST
            bool anyExecuted = false; //czy wykonalismy jakies zapytania flaga czy wykonalismy jakies zapytania 

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
        _db.MetricSamples.AddRange(samples);
        await _db.SaveChangesAsync(cancellationToken); //zapisujemy próbki metryk do bazy danych
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
}
    


