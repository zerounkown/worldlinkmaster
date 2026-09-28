using Npgsql;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// Shared setup for tests that build their own throwaway Postgres schema and
/// <c>ApplicationDbContext</c> directly (rather than going through the real app process, the way
/// <see cref="E2EWebAppFactory"/> does for the Playwright journeys) — currently
/// BulkUpdateExecutionStrategyTests and BulkUpdateColumnsTests.
///
/// Root-caused from CI: adding a second such test class alongside the first made
/// "CREATE EXTENSION IF NOT EXISTS pg_trgm" (see ApplicationDbContext.OnModelCreating) start
/// failing intermittently with "23505: duplicate key value violates unique constraint
/// 'pg_extension_name_index'". Postgres extensions are database-wide, not schema-scoped, and
/// IF NOT EXISTS isn't safe against concurrent callers: xUnit runs different test classes'
/// IAsyncLifetime.InitializeAsync concurrently by default, so two (or, counting
/// E2EWebAppFactory's own Migrate() call, three) sessions can all see "doesn't exist" and all try
/// to create it before any of them commits — exactly the kind of race that got more likely to
/// actually hit once a second Postgres-backed test class was added. The loser's EF
/// EnsureCreatedAsync() call then throws that exception and aborts entirely, before creating a
/// single table — which is why the failure cascaded into "relation \"AspNetUsers\" does not
/// exist" for every query the affected test then made.
///
/// EnsureTrgmExtensionAsync pre-creates the extension in its own statement, outside of
/// EnsureCreatedAsync, and tolerates exactly that race: a 23505 there means the extension is now
/// guaranteed to exist, because Postgres only reports a unique-constraint conflict after the
/// other, conflicting transaction has committed (unique checks wait for the competing transaction
/// to resolve, then re-check) — so losing this race is a success case, not a real failure. Calling
/// it before EnsureCreatedAsync() means that call's own redundant internal
/// "CREATE EXTENSION IF NOT EXISTS" finds the extension already durably committed, a genuine
/// no-op instead of a second roll of the same dice.
/// </summary>
internal static class PostgresTestSchema
{
    /// <summary>Drops and recreates <paramref name="schemaName"/> as an empty schema, having first ensured pg_trgm exists.</summary>
    public static async Task ResetAsync(string connectionString, string schemaName)
    {
        await EnsureTrgmExtensionAsync(connectionString);

        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schemaName} CASCADE", admin))
        {
            await drop.ExecuteNonQueryAsync();
        }
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schemaName}", admin))
        {
            await create.ExecuteNonQueryAsync();
        }
    }

    public static async Task DropAsync(string connectionString, string schemaName)
    {
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schemaName} CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }

    public static async Task EnsureTrgmExtensionAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        try
        {
            await using var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS pg_trgm", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // See the class-level remarks — this means a concurrent caller already committed it.
        }
    }
}
