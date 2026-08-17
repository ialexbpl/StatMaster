using StatMaster.Agent;

var options = AgentRuntimeOptions.Load(args);

//create objects
var registry = new MetricRegistry();
var requestHandler = new MetricRequestHandler(registry);
var agentClient = new AgentClient(options, requestHandler);

//run agent
await agentClient.RunAsync();

//Responsibility of file: bootstrap/composition only (no protocol logic).