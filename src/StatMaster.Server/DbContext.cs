using Microsoft.EntityFrameworkCore;

namespace StatMaster.Server;

// Centralny kontekst EF Core: mapuje tabele metryk/agentow i pilnuje kluczowych ograniczen DB.
public sealed class StatMasterDbContext : DbContext //EF MOWI ZE TO GLOWNY KONTEKST DB
{
    public StatMasterDbContext(DbContextOptions<StatMasterDbContext> options) : base(options) { }//konstruktor kontekstu DB przechwytuje opcje z konfiguracji i przekazuje je do klasy bazowej DbContext z program.cs

    public DbSet<MetricDefinitionModel> MetricDefinitions => Set<MetricDefinitionModel>();//tu ida zapisy i odczyt do bazy, katalog i scheduler co ile i kied
    public DbSet<AgentTargetModel> AgentTargets => Set<AgentTargetModel>();//tu idzie lista endpointow i agentow

    protected override void OnModelCreating(ModelBuilder modelBuilder) //modelBuilder to EF Core API, które pozwala na konfigurację mapowania modeli na tabele w bazie danych
    {
        modelBuilder.Entity<MetricDefinitionModel>()
            .HasIndex(x => x.Key) //unikalny indeks na polu Key
            .IsUnique();

        modelBuilder.Entity<AgentTargetModel>()
            .HasIndex(x => x.AgentId)//unikalny indeks na polu AgentId
            .IsUnique();

    }
}
/*

Jak to się łączy z resztą systemu:

Program.cs tworzy StatMasterDbContext przez UseSqlite(...).
Na starcie:
EnsureCreated() tworzy strukturę DB,
bootstrap seeduje metryki, jeśli pusto,
reader bierze metryki do schedulera.
Scheduler odpytuje agenta wg definicji z DB.

*/