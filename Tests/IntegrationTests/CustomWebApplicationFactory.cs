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

    // The real Data/Migrations history doesn't fully match the current EF model — several
    // columns/tables were applied straight to the production database via raw SQL over the
    // project's history and were never captured in a migration (found while auditing this for
    // a real deployment earlier). Replaying the recorded migrations against a fresh database
    // (what Database.MigrateAsync() does) therefore produces an incomplete schema. Instead,
    // build the schema directly from the current C# model (always accurate) via EnsureCreated,
    // then pre-populate __EFMigrationsHistory so the app's own MigrateAsync() call in
    // SeedData.InitializeAsync sees every migration as already applied and no-ops cleanly
    // instead of trying to re-run CREATE TABLE against tables that already exist.
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
                $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {"8.0.11"})");
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
