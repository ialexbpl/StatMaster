using StatMaster.Agent;

// if no args, use default values LOCALHOST:50001
string serverHost = args.Length > 0 ? args[0] : "127.0.0.1";
int serverPort = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 50001;

//create objects
var registry = new MetricRegistry();
var requestHandler = new MetricRequestHandler(registry);
var agentClient = new AgentClient(serverHost, serverPort, requestHandler);

//run agent
await agentClient.RunAsync();

//Responsibility of file: bootstrap/composition only (no protocol logic).