using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// Shared setup for tests that build their own throwaway Postgres schema and
/// <c>ApplicationDbContext</c> directly (rather than going through the real app process, the way
/// <see cref="E2EWebAppFactory"/> does for the Playwright journeys) — currently
/// BulkUpdateExecutionStrategyTests and BulkUpdateColumnsTests.
///
/// Both of those join <see cref="E2ETestCollection"/> specifically so xUnit serializes them
/// against each other AND against the Playwright journeys, and initializes E2EWebAppFactory (and
/// therefore runs its Migrate()) before any of them starts. That fixed the original race — two
/// independent EnsureCreatedAsync() callers, plus Migrate(), all issuing the database-wide
/// "CREATE EXTENSION IF NOT EXISTS pg_trgm" (ApplicationDbContext.OnModelCreating) concurrently —
/// but it exposed two more problems that only show up once these calls are serialized rather than
/// racing, both of which this class works around:
///
/// 1. EnsureCreatedAsync()'s "does this already exist" check isn't schema-aware — it looks for
///    tables matching the model ANYWHERE in the current database, not specifically in the current
///    search_path's schema. Once Migrate() has already built a full copy of every table in
///    "e2e_test", EnsureCreatedAsync() against an empty "bulk_update_*_test" schema sees those
///    same table names already present in the database and silently returns without creating a
///    single table in ITS OWN (still-empty) schema — surfacing later as
///    "relation ... does not exist" the first time a test queries anything. CreateSchemaAsync
///    below sidesteps this by generating the model's CREATE script and executing it directly
///    (GenerateCreateScript never checks whether anything already exists), rather than calling
///    EnsureCreatedAsync().
///
/// 2. A Postgres extension installs into whichever schema is first in the CREATING session's
///    search_path — for Migrate(), always "e2e_test" (see E2EWebAppFactory). Once Migrate() has
///    already won the race to create pg_trgm there, a later CREATE INDEX ... USING gin (...
///    gin_trgm_ops) issued by a session whose search_path doesn't include "e2e_test" fails with
///    "42704: operator class 'gin_trgm_ops' does not exist for access method 'gin'" — the
///    extension NAME is shared and satisfies "IF NOT EXISTS" everywhere, but its actual objects
///    are only visible to sessions that can see the schema it was installed into.
///    FindExtensionSchemaAsync looks up that schema at runtime (rather than hard-coding "e2e_test"
///    or "public", which would just be guessing at another class's implementation detail), so
///    CreateSchemaAsync can add it to the connection's search_path as a fallback.
/// </summary>
internal static class PostgresTestSchema
{
    /// <summary>
    /// Drops and recreates <paramref name="schemaName"/> as an empty schema, then builds it from
    /// <paramref name="context"/>'s current model via a directly-executed CREATE script — see the
    /// class remarks for why EnsureCreatedAsync() itself isn't reliable here.
    /// </summary>
    public static async Task CreateSchemaAsync(ApplicationDbContext context, string connectionString, string schemaName)
    {
        await using (var admin = new NpgsqlConnection(connectionString))
        {
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

        var script = context.Database.GenerateCreateScript();
        await context.Database.ExecuteSqlRawAsync(script);
    }

    public static async Task DropSchemaAsync(string connectionString, string schemaName)
    {
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schemaName} CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// The name of the schema <paramref name="extensionName"/> is currently installed into, or
    /// null if it hasn't been created anywhere in this database yet (in which case the caller
    /// will be the one to create it, landing naturally in its own schema — see remarks).
    /// </summary>
    public static async Task<string?> FindExtensionSchemaAsync(string connectionString, string extensionName)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT n.nspname FROM pg_extension e JOIN pg_namespace n ON n.oid = e.extnamespace WHERE e.extname = @name",
            conn);
        cmd.Parameters.AddWithValue("name", extensionName);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// The search_path value for a connection that needs <paramref name="schemaName"/> as its own
    /// (first, so unqualified table references resolve there) schema, plus whichever schema
    /// <paramref name="extensionName"/> is already installed into (if any, and if different),
    /// as a fallback so extension-provided operator classes like gin_trgm_ops remain resolvable.
    /// </summary>
    public static async Task<string> BuildSearchPathAsync(string connectionString, string schemaName, string extensionName)
    {
        var extensionSchema = await FindExtensionSchemaAsync(connectionString, extensionName);
        return extensionSchema != null && extensionSchema != schemaName
            ? $"{schemaName},{extensionSchema}"
            : schemaName;
    }
}
