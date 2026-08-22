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
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // A SQLite ":memory:" database only exists while its connection stays open, so the
    // connection itself must live as long as the factory does, not be opened per-DbContext.
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

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
            // Program.cs registers ApplicationDbContext via AddDbContextFactory + a scoped
            // context derived from that factory (rather than a plain AddDbContext) so
            // ProductsController.Index can fan its facet-count queries out to their own
            // short-lived contexts. Swapping in SQLite for tests has to remove and replace both
            // registrations the same way — removing only DbContextOptions<ApplicationDbContext>
            // leaves the real app's singleton IDbContextFactory<ApplicationDbContext> in place,
            // which then conflicts with whatever DbContextOptions gets registered next (the same
            // "singleton can't depend on scoped options" error Program.cs itself had to avoid).
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            var dbContextFactoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDbContextFactory<ApplicationDbContext>));
            if (dbContextFactoryDescriptor != null)
            {
                services.Remove(dbContextFactoryDescriptor);
            }

            services.AddDbContextFactory<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });
            services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext());

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
    // "OR IGNORE" (not a plain INSERT): CI intermittently failed with "UNIQUE constraint failed:
    // __EFMigrationsHistory.MigrationId" (never reproduced locally) — WebApplicationFactory's
    // lazy first-build-on-first-access isn't documented as safe under concurrent access, and
    // xUnit runs different test classes (each with its own CustomWebApplicationFactory instance)
    // as separate collections in parallel by default, so ConfigureWebHost — and therefore this
    // method — plausibly ran more than once for the same instance/connection under CI's
    // parallelism. Making the insert idempotent neutralizes that regardless of the exact
    // mechanism, without touching WebApplicationFactory's host-build timing (attempted via
    // IAsyncLifetime first; reverted — forcing an eager build broke ConfigureWebHost's connection-
    // string placeholder from applying before Program.cs's own startup check ran).
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
