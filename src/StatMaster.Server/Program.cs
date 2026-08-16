using Microsoft.Extensions.Configuration;
using StatMaster.Server;
using Microsoft.EntityFrameworkCore;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build(); //ladujemy configuration z appsettings.json i env

string dbConnection = configuration.GetConnectionString("StatMasterDb")
    ?? "Data Source=statmaster.db";

var dbOptions = new DbContextOptionsBuilder<StatMasterDbContext>()
    .UseSqlite(dbConnection)
    .Options;
using var db = new StatMasterDbContext(dbOptions);
db.Database.EnsureCreated();

// fallback onboarding: jeśli DB puste, zasiej z appsettings
MetricCatalogBootstrapper.SeedFromAppsettingsIfEmpty(db, configuration);
List<MetricModel> itemsToAsk = DbMetricCatalogReader.ResolveEnabledItems(db);

// safety fallback: jeśli DB da 0, bierzemy appsettings
if (itemsToAsk.Count == 0)
{
    itemsToAsk = MetricConfigReader.ResolveEnabledItems(configuration);
}
if (itemsToAsk.Count == 0)
{
    Console.WriteLine("[Server] No enabled metrics found.");
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