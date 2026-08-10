using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// Boots the real app (real Program.cs, real DI graph, real Npgsql/Postgres — no SQLite
/// substitution) as a real child process listening on a real Kestrel socket, so an actual browser
/// (Playwright) can navigate to it.
///
/// Runs as a subprocess rather than in-process via WebApplicationFactor&lt;Program&gt;: in-process
/// hosting was tried first, but WebApplicationFactory's own internal host-building always wins the
/// IServer registration race with the in-memory TestServer, even after ConfigureWebHost(wb =&gt;
/// wb.UseKestrel()) — the "Now listening on" log printed the literal unresolved "http://127.0.0.1:0"
/// and no real socket ever opened. A separate process sidesteps that entirely.
///
/// Points the app at a dedicated "e2e_test" schema, isolated from "public" via the same
/// DROP/CREATE SCHEMA technique already proven safe during the migration-reconciliation work.
/// Booting against that empty schema makes the app's own SeedData.InitializeAsync() (called
/// unconditionally at the end of Program.cs) run the real migration chain and seed the baseline
/// catalog automatically — no separate EF CLI step needed here.
///
/// The target Postgres instance comes entirely from the E2E_POSTGRES_CONNECTION environment
/// variable — no default is baked into source (this file is committed to a shared repo, and the
/// local-dev value points at a real, credentialed Supabase instance). Locally, set it to the same
/// shared Supabase instance used elsewhere in this project (isolation there comes entirely from
/// the dedicated schema below, since there's no throwaway database to spin up there); in CI
/// (.github/workflows/_e2e-tests.yml) it's set to a fresh, disposable Postgres service container
/// instead — but using the identical code path in both places avoids CI-only/local-only behavior
/// drift.
/// </summary>
public class E2EWebAppFactory : IAsyncLifetime
{
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("E2E_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "The E2E_POSTGRES_CONNECTION environment variable is not set. Point it at a Postgres " +
            "instance the E2E suite is allowed to create/drop an \"e2e_test\" schema in (e.g. the " +
            "same connection string used for local development, or a local/CI Postgres container).");

    private const string SchemaName = "e2e_test";

    // Tests.E2E/bin/Debug/net8.0/ -> up 4 levels -> repo root. Building Tests.E2E (via its
    // ProjectReference) always also builds WorldLinkMaster.Web into its own bin/Debug/net8.0/
    // as a side effect, so this path is guaranteed to exist once the test project has built.
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static readonly string WebDllPath =
        Path.Combine(RepoRoot, "bin", "Debug", "net8.0", "WorldLinkMaster.Web.dll");

    private Process? _process;

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (!File.Exists(WebDllPath))
        {
            throw new FileNotFoundException(
                $"Expected the built app at {WebDllPath} — build WorldLinkMaster.Web.csproj (or Tests.E2E, which references it) first.",
                WebDllPath);
        }

        await ResetSchemaAsync();

        // Trailing slash matters: both plain string concatenation (AuthPages) and
        // Uri-combining (E2ETestBase.Url) treat "http://host:port" + "Products" as replacing the
        // (nonexistent) last path segment without it, silently producing "http://host:portProducts".
        BaseUrl = $"http://127.0.0.1:{GetFreeTcpPort()}/";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(WebDllPath);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"{AdminConnectionString};Search Path={SchemaName}";

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the app process.");

        // Drain stdout/stderr continuously — otherwise the child's console output eventually
        // fills the redirected pipe's buffer and blocks the app.
        _ = DrainAsync(_process.StandardOutput);
        _ = DrainAsync(_process.StandardError);

        await WaitUntilReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }

        _process?.Dispose();
        await DropSchemaAsync();
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is not null)
            {
                // Discard — the process's own console output isn't needed for the test run.
            }
        }
        catch (ObjectDisposedException)
        {
            // Process was killed while a read was in flight — expected during teardown.
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException($"App process exited early with code {_process.ExitCode} before becoming ready.");
            }

            try
            {
                var response = await client.GetAsync("/ready");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Server still starting up — retry.
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"App did not become ready at {BaseUrl}/ready within 90s.");
    }

    private static async Task ResetSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand($"CREATE SCHEMA {SchemaName}", conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // EF's own "does __EFMigrationsHistory exist" existence check (NpgsqlHistoryRepository)
        // doesn't respect the connection string's Search Path the way ordinary DML/DDL does — it
        // always checks nspname='public' specifically. Without this, Database.MigrateAsync()
        // (called by SeedData.InitializeAsync at app startup) throws
        // "relation \"__EFMigrationsHistory\" does not exist" against a fresh non-public schema,
        // even though the table is about to be created there. Same workaround already proven
        // during the migration-reconciliation work: pre-create it empty so the existence check
        // (which only looks in "public") is bypassed and the real migration run proceeds normally.
        await using (var cmd = new NpgsqlCommand(
            $"""
             CREATE TABLE {SchemaName}."__EFMigrationsHistory" (
                 "MigrationId" character varying(150) NOT NULL,
                 "ProductVersion" character varying(32) NOT NULL,
                 CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
             )
             """, conn))
        {
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
