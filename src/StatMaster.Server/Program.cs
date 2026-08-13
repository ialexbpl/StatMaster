using Microsoft.Extensions.Configuration;
using StatMaster.Server;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build(); //ladujemy configuration z appsettings.json i env

//zwraca liste metric modeli ktore sa enabled w configuration zapisuje do itemsToAsk
//usunieto keysToAsk bo nie potrzebujemy juz tego wyswietlac w konsoli bo jest to obsluzane przez scheduler bo on potrzbuje teraz wszystkie metryki od razu
List<MetricModel> itemsToAsk = MetricConfigReader.ResolveEnabledItems(configuration);
if (itemsToAsk.Count == 0) //jesli nie ma zadnych metryk enabled to konczy program
{
    Console.WriteLine("[Server] No enabled keys matched profile/target scope.");
    return;
}
//were only asking for the keys that are enabled in the configuration and assigning them to the keysToAsk array
//setup the server and listen for the agent and query the metric
//tzw "manualne wstrzyknięcie zależności"
var runtime = ServerRuntimeOptions.FromConfiguration(configuration);//bierzemy port, certyfikat i token z configu
var queryOptions = MetricQueryTimeout.FromConfiguration(configuration);//bierzemy timeout z configu

var queryService = new MetricQueryService(queryOptions);//tworzymy queryService z timeoutem
var scheduler = new MetricScheduler(queryService);//tworzymy scheduler z queryService
var listener = new AgentListener(runtime.Port, queryService, runtime.LoadCertificate(), runtime.ExpectedToken);//tworzymy listener z portem, queryService, certyfikatem i tokenem

await listener.ListenAndServeAsync(//po handshake w przekazuje stream do scheduler ktory bedzie odpytywac metryki
    sessionHandler: (stream, cancellationToken) => scheduler.RunAsync(stream, itemsToAsk, cancellationToken));//scheduler bedzie odpytywac metryki w okreslonych odstepach czasu

//responsibility of file: setup the server 