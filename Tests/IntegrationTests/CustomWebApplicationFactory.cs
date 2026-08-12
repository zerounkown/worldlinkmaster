using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Tests.IntegrationTests;

/// <summary>
/// Boots the real app (real Program.cs, real DI graph) but swaps the Supabase/Npgsql DbContext
/// for a SQLite in-memory database per factory instance, and adds a fake authentication scheme
/// (see TestAuthHandler) so tests can authenticate as an arbitrary user without a real login
/// flow. SQLite — not the EF InMemory provider — because SeedData.InitializeAsync calls
/// Database.MigrateAsync() at startup, which only works against a real relational provider.
/// Every other service (Stripe, email, SignalR, etc.) runs as configured in Program.cs.
///
/// Implements IAsyncLifetime so xUnit builds the host (InitializeAsync) itself, before any test
/// method runs, rather than lazily on the first WebApplicationFactory.CreateClient()/Server access
/// from a test. WebApplicationFactory's lazy first-build path isn't documented as safe against
/// concurrent first access, and different test classes' fixtures (this one, one per class via
/// IClassFixture) do build concurrently — xUnit runs separate test classes as separate collections
/// in parallel by default. Forcing the build inside InitializeAsync moves it to a point xUnit
/// itself awaits deterministically, out of the class's own tests entirely.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // A SQLite ":memory:" database only exists while its connection stays open, so the
    // connection itself must live as long as the factory does, not be opened per-DbContext.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    async Task IAsyncLifetime.InitializeAsync()
    {
        // Touching Server is what actually triggers WebApplicationFactory's lazy host build
        // (and therefore ConfigureWebHost/CreateSchemaFromCurrentModel below) — awaited here so
        // it's fully finished before xUnit runs this class's first test.
        _ = Server;
        await Task.CompletedTask;
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        _connection.Open();
        CreateSchemaFromCurrentModel();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Program.cs throws at startup if this is empty — the real value is replaced by the
            // SQLite provider below, but a placeholder must exist for the initial registration.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=placeholder;Username=placeholder;Password=placeholder",
            });
        });

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    // SQLite can't faithfully replay the real Postgres migrations (decimal ordering/aggregates
    // aren't supported — see the skipped tests in OrdersControllerTests/ProductsControllerTests),
    // so build the schema directly from the current C# model (always accurate) via EnsureCreated,
    // then pre-populate __EFMigrationsHistory so the app's own MigrateAsync() call in
    // SeedData.InitializeAsync sees every migration as already applied and no-ops cleanly instead
    // of trying to re-run CREATE TABLE against tables that already exist.
    //
    // "OR IGNORE" (not a plain INSERT) as defense-in-depth: this only needs to run once per
    // factory instance, and IAsyncLifetime.InitializeAsync above is what actually guarantees that
    // now — but staying idempotent here costs nothing and protects against exactly the kind of
    // double-invocation bug that caused CI's "UNIQUE constraint failed" failures before that fix.
    private void CreateSchemaFromCurrentModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        using var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();

        var historyRepository = context.GetService<IHistoryRepository>();
        context.Database.ExecuteSqlRaw(historyRepository.GetCreateIfNotExistsScript());

        var migrationsAssembly = context.GetService<IMigrationsAssembly>();
        foreach (var migrationId in migrationsAssembly.Migrations.Keys)
        {
            context.Database.ExecuteSql(
                $"INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {"8.0.11"})");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
