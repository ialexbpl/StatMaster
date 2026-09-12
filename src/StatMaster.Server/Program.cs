using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using StatMaster.Server;
using StatMaster.Server.Endpoints;
using StatMaster.Server.Queries;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine("UI", "wwwroot")
});//wires automatically appsettings
/*
Automatycznie wczytuje:

appsettings.json

appsettings.Development.json

zmienne środowiskowe

user secrets (jeśli dev)

parametry z linii komend
*/

string configuredDbConnection = builder.Configuration.GetConnectionString("StatMasterDb")
    ?? "Data Source=statmaster.db";
string dbConnection = ResolveDatabaseConnection(configuredDbConnection);

builder.Services.AddDbContextFactory<StatMasterDbContext>(options =>
    options.UseSqlite(dbConnection));
builder.Services.AddScoped<DashboardReadService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<IPasswordHasher<AdminUserModel>, PasswordHasher<AdminUserModel>>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = "/UI/Pages";
});
builder.Services.AddServerSideBlazor();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "statmaster.auth";
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DashboardAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("must_change_password", "false"); //during the authentication process, the user is authenticated and the claims are added to the principal
    });
});

var dbOptions = new DbContextOptionsBuilder<StatMasterDbContext>()
    .UseSqlite(dbConnection)
    .Options;

using var schedulerDb = new StatMasterDbContext(dbOptions);
schedulerDb.Database.EnsureCreated();
schedulerDb.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
schedulerDb.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
EnsureAdminUsersTable(schedulerDb);
SeedDefaultAdminIfMissing(schedulerDb);

// fallback onboarding: jeśli DB puste, zasiej z appsettings
MetricCatalogBootstrapper.SeedFromAppsettingsIfEmpty(schedulerDb, builder.Configuration);
List<MetricModel> itemsToAsk = DbMetricCatalogReader.ResolveEnabledItems(schedulerDb);

// safety fallback: jeśli DB da 0, bierzemy appsettings
if (itemsToAsk.Count == 0)
{
    itemsToAsk = MetricConfigReader.ResolveEnabledItems(builder.Configuration);
}
if (itemsToAsk.Count == 0)
{
    Console.WriteLine("[Server] No enabled metrics found.");
    return;
}

//were only asking for the keys that are enabled in the configuration and assigning them to the keysToAsk array
//setup the server and listen for the agent and query the metric
//tzw "manualne wstrzyknięcie zależności"
var runtime = ServerRuntimeOptions.FromConfiguration(builder.Configuration);//bierzemy port, certyfikat i token z configu
var queryOptions = MetricQueryTimeout.FromConfiguration(builder.Configuration);//bierzemy timeout z configu

var queryService = new MetricQueryService(queryOptions);//tworzymy queryService z timeoutem

builder.Services.AddSingleton(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<StatMasterDbContext>>();
    return new AgentListener(
        runtime.Port,
        queryService,
        runtime.LoadCertificate(),
        runtime.ExpectedToken,
        factory);
});

var app = builder.Build();
var dbFactory = app.Services.GetRequiredService<IDbContextFactory<StatMasterDbContext>>();
var listener = app.Services.GetRequiredService<AgentListener>();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapDashboardEndpoints();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

_ = Task.Run(async () =>
{
    try
    {
        await listener.ListenAndServeAsync(
            sessionHandler: (agentId, stream, cancellationToken) =>
            {
                var sessionScheduler = new MetricScheduler(queryService, dbFactory, listener);
                return sessionScheduler.RunAsync(agentId, stream, itemsToAsk, cancellationToken);
            },
            cancellationToken: app.Lifetime.ApplicationStopping);
    }
    catch (OperationCanceledException)
    {
        // app is shutting down
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Server] Listener stopped: {ex.Message}");
    }
}, app.Lifetime.ApplicationStopping);

await app.RunAsync();

//responsibility of file: setup the server 

static string ResolveDatabaseConnection(string connectionString)
{
    string projectDbPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "statmaster.db"));

    var builder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(builder.DataSource))
    {
        builder.DataSource = projectDbPath;
        return builder.ToString();
    }

    if (string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase) ||
        Path.IsPathRooted(builder.DataSource))
    {
        return builder.ToString();
    }

    string projectDirectory = Path.GetDirectoryName(projectDbPath)!;
    builder.DataSource = Path.GetFullPath(Path.Combine(projectDirectory, builder.DataSource));
    return builder.ToString();
}

static void EnsureAdminUsersTable(StatMasterDbContext db)
{
    db.Database.ExecuteSqlRaw(
        """
        CREATE TABLE IF NOT EXISTS AdminUsers (
            Id INTEGER NOT NULL CONSTRAINT PK_AdminUsers PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            PasswordHash TEXT NOT NULL,
            MustChangePassword INTEGER NOT NULL DEFAULT 1,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL
        );
        """);

    db.Database.ExecuteSqlRaw(
        """
        CREATE UNIQUE INDEX IF NOT EXISTS IX_AdminUsers_Username
        ON AdminUsers (Username);
        """);
}

static void SeedDefaultAdminIfMissing(StatMasterDbContext db)
{
    if (db.AdminUsers.Any())
        return;

    var hasher = new PasswordHasher<AdminUserModel>();
    var admin = new AdminUserModel
    {
        Username = "admin",
        PasswordHash = string.Empty,
        MustChangePassword = true,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
    admin.PasswordHash = hasher.HashPassword(admin, "admin");
    db.AdminUsers.Add(admin);
    db.SaveChanges();
}