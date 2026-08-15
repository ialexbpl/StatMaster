using System.Text.Json;

namespace StatMaster.Agent;

public static class ScriptConfigLoader
{
    public static IReadOnlyList<ScriptMetricDefinition> Load(string fileName = "agent-scripts.json")
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            return Array.Empty<ScriptMetricDefinition>();

        string json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var items = JsonSerializer.Deserialize<List<ScriptMetricDefinition>>(json, options)
                    ?? new List<ScriptMetricDefinition>();

        return items
            .Where(i => i.Enabled)
            .Where(i => !string.IsNullOrWhiteSpace(i.Key))
            .Where(i => !string.IsNullOrWhiteSpace(i.FileName))
            .ToList();
    }
}

//klasa wczytująca custom key skryptów z pliku JSON