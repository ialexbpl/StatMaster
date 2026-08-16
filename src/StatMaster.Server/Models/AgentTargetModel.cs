namespace StatMaster.Server;

//ORM (Object-Relational Mapping), czyli w świecie .NET biblioteki Entity Framework Core.
public sealed class AgentTargetModel
{
    // przygotowanie pod panel admina (host/ip/port/enabled)
    public int Id { get; set; }
    public required string AgentId { get; set; }
    public required string DisplayName { get; set; }
    public required string HostOrIp { get; set; }
    public int Port { get; set; } = 50001;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
//taki model migruje do tabeli agent_targets w db komenda np dotnet ef migrations add InitialCreate. a potem dotnet ef database update

//prosty przyklad w sql dla tabeli np agent_targets

/*
CREATE TABLE "AgentTargets" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AgentTargets" PRIMARY KEY AUTOINCREMENT,
    "AgentId" TEXT NOT NULL,
    "DisplayName" TEXT NOT NULL,
    "HostOrIp" TEXT NOT NULL,
    "Port" INTEGER NOT NULL DEFAULT 50001,
    "Enabled" INTEGER NOT NULL DEFAULT 1
    "CreatedAtUtc" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAtUtc" DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);
*/
