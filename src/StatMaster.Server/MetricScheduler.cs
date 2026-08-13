namespace StatMaster.Server;


//klasa dla harmonogramu zap to agenta ktory bedzie odpytywac metryki w okreslonych odstepach czasu
public sealed class MetricScheduler
{
    private readonly MetricQueryService _queryService; //klasa typu MetricQueryService

    public MetricScheduler(MetricQueryService queryService) //wstrzykniecie zaleznosci
    {
        _queryService = queryService;//przypisanie zaleznosci
    }

    public async Task RunAsync(
        Stream stream,
        IReadOnlyList<MetricModel> items,
        CancellationToken cancellationToken = default)
    {
        var groups = items //grupujemy metryki po odstepach czasu i tworzymy dictionary gdzie kluczem jest odstep czasu a wartoscia jest tablica kluczy metryk
            .GroupBy(i => Math.Max(1, i.IntervalSeconds))//grupujemy metryki po odstepach czasu i ochrzaniamy je na minimum 1 sekunde
            .ToDictionary(g => g.Key, g => g.Select(x => x.Key).ToArray());//tu robie dictionary gdzie kluczem jest odstep czasu a wartoscia jest tablica kluczy metryk

        var nextRun = groups.Keys.ToDictionary(k => k, _ => DateTimeOffset.UtcNow); //nextrun zapisuje czas kiedy nastepny raz bedzie odpytywac metryki 

        while (!cancellationToken.IsCancellationRequested) //dopoki sesja nie jest anulowana
        {
            DateTimeOffset now = DateTimeOffset.UtcNow; //pobieramy aktualny czas
            bool anyExecuted = false; //czy wykonalismy jakies zapytania flaga czy wykonalismy jakies zapytania 

            foreach (var group in groups) //pętla po grupach metryk
            {
                int intervalSeconds = group.Key; //pobieramy odstep czasu
                string[] keys = group.Value; //pobieramy tablice kluczy metryk

                if (now < nextRun[intervalSeconds]) //jesli aktualny czas jest mniejszy niz czas kiedy nastepny raz bedzie odpytywac metryki
                    continue; //to przejdz do nastepnej grupy

                var responses = await _queryService.QueryMetricsAsync(stream, keys, cancellationToken); //odpytujemy metryki wysyla batch kluczy do queryService
                MetricResponsePrinter.Print(responses); //wypisujemy odpowiedzi

                nextRun[intervalSeconds] = now.AddSeconds(intervalSeconds); //zapisujemy czas kiedy nastepny raz bedzie odpytywac metryki
                anyExecuted = true;
            }

            if (!anyExecuted)
                await Task.Delay(250, cancellationToken); //krotka pauza 250ms dla cpu
        }
    }
}

