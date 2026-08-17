using System.Text.Json;

namespace StatMaster.Agent;

public sealed class AgentRuntimeOptions
{
    public string ServerHost { get; set; } = "127.0.0.1";
    public int ServerPort { get; set; } = 50001;
    public string AgentId { get; set; } = "AGENT-01";
    public string Token { get; set; } = "dev-token";
    public string? TlsTargetHost { get; set; }
    public int ReconnectDelaySeconds { get; set; } = 5;

    public static AgentRuntimeOptions Load(string[] args)
    {
        var options = new AgentRuntimeOptions();
        string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(configPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("Agent", out var agent))
            {
                options.ServerHost = ReadString(agent, "ServerHost") ?? options.ServerHost;
                options.ServerPort = ReadInt(agent, "ServerPort") ?? options.ServerPort;
                options.AgentId = ReadString(agent, "AgentId") ?? options.AgentId;
                options.Token = ReadString(agent, "Token") ?? options.Token;
                options.TlsTargetHost = ReadString(agent, "TlsTargetHost");
                options.ReconnectDelaySeconds = ReadInt(agent, "ReconnectDelaySeconds") ?? options.ReconnectDelaySeconds;
            }
        }

        // CLI override for quick local tests:
        // dotnet run --project src/StatMaster.Agent -- 10.0.0.5 50001
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            options.ServerHost = args[0];
        if (args.Length > 1 && int.TryParse(args[1], out var cliPort))
            options.ServerPort = cliPort;

        options.ReconnectDelaySeconds = Math.Max(1, options.ReconnectDelaySeconds);
        options.TlsTargetHost = string.IsNullOrWhiteSpace(options.TlsTargetHost)
            ? options.ServerHost
            : options.TlsTargetHost;

        return options;
    }

    private static string? ReadString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var result = value.GetString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static int? ReadInt(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
