using Npgsql;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// Shared drop/create-schema boilerplate for tests that build their own throwaway Postgres schema
/// and <c>ApplicationDbContext</c> directly (rather than going through the real app process, the
/// way <see cref="E2EWebAppFactory"/> does for the Playwright journeys) — currently
/// BulkUpdateExecutionStrategyTests and BulkUpdateColumnsTests.
///
/// Both of those join <see cref="E2ETestCollection"/> specifically so xUnit serializes them
/// against each other AND against the Playwright journeys, and initializes E2EWebAppFactory (and
/// therefore runs its Migrate()) before any of them starts. That's not just about avoiding wasted
/// CI time — it sidesteps a real bug this suite hit once two independent EnsureCreatedAsync()
/// callers existed and ran concurrently with each other and with Migrate(): all three issue
/// "CREATE EXTENSION IF NOT EXISTS pg_trgm" (see ApplicationDbContext.OnModelCreating), which
/// Postgres extensions being database-wide (not schema-scoped) makes racy in more than one way —
/// concurrent callers can both see "doesn't exist" and both try to create it (one throws
/// "23505: duplicate key value violates unique constraint 'pg_extension_name_index'", aborting
/// EnsureCreatedAsync()/Migrate() entirely before a single table exists), and even a "successful"
/// IF NOT EXISTS no-op on one connection doesn't guarantee a DIFFERENT, already-open connection's
/// cached type/operator-class info reflects it yet, which surfaced as
/// "42704: operator class 'gin_trgm_ops' does not exist for access method 'gin'" when a fix
/// attempt here tried to pre-create the extension from a separate, unscoped connection instead of
/// serializing. Running these one at a time, only after Migrate() has already fully committed,
/// avoids both failure modes by construction rather than by chance.
/// </summary>
internal static class PostgresTestSchema
{
    /// <summary>Drops and recreates <paramref name="schemaName"/> as an empty schema.</summary>
    public static async Task ResetAsync(string connectionString, string schemaName)
    {
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
}
