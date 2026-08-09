using Microsoft.Extensions.Configuration;
using StatMaster.Server;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

string[] keysToAsk = MetricCatalogResolver.ResolveEnabledKeys(configuration);
if (keysToAsk.Length == 0)
{
    Console.WriteLine("[Server] No enabled keys matched profile/target scope.");
    return;
}

var runtime = ServerRuntimeOptions.FromConfiguration(configuration);
var queryOptions = MetricQueryTimeout.FromConfiguration(configuration);
var queryService = new MetricQueryService(queryOptions);
var listener = new AgentListener(runtime.Port, queryService, runtime.LoadCertificate(), runtime.ExpectedToken);

var responses = await listener.ListenAndQueryManyAsync(keysToAsk);
MetricResponsePrinter.Print(responses);

// Responsibility of file: setup the server and listen for the agent and query the metric