namespace StatMaster.Server;

public sealed class MetricDefinitionModel
{
    // Katalog metryk w DB (source-of-truth docelowo), z fallbackiem na appsettings.
    public int Id { get; set; }
    public required string Key { get; set; }
    public required string Description { get; set; }
    public required string ValueType { get; set; }
    public required string Unit { get; set; }
    public bool Enabled { get; set; } = true;
    public required string Source { get; set; } // built-in / script
    public string Target { get; set; } = "all";
    public int IntervalSeconds { get; set; } = 30;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
//tu taki sam zamysl jak w MetricDefinitionModel, tylko dla scriptów.
/*

CREATE TABLE MetricDefinitions (
Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL UNIQUE,
    "Name" TEXT NOT NULL,
    Description TEXT NULL,
    "Shell" TEXT NOT NULL DEFAULT 'powershell',
    ScriptContent TEXT NOT NULL,
    Arguments TEXT NULL,
    TimeoutSeconds INTEGER NOT NULL DEFAULT 60,
    Enabled INTEGER NOT NULL DEFAULT 1,
    Target TEXT NOT NULL DEFAULT 'all',
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);
*/

