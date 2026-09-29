using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.E2E.IntegrationTests;

/// <summary>
/// Regression coverage for Admin -&gt; Master Data Import crashing with the generic ASP.NET Core
/// "An error occurred while processing your request" page instead of showing an error on the
/// result page.
///
/// Root cause: SizeGroup.UnitType is [StringLength(20)] (Models/SizeGroup.cs), which EF's
/// Postgres migration turned into a real "character varying(20)" column
/// (20260728071547_AddMasterDataAndProductImportSchema.cs). A hand-typed "Unit / Type" cell like
/// "Fitted hat size (inches)" (25 characters) was accepted by ClosedXML and by every C# check in
/// MasterDataController.ImportSizeGroupsAsync — nothing there ever looked at string length — so it
/// sailed straight through to SaveChangesAsync, where Postgres genuinely enforces the column's
/// declared length and throws (Npgsql.PostgresException, SqlState 22001, "value too long for type
/// character varying(20)"), wrapped by EF as a DbUpdateException. SQLite (the fast Tests/ unit
/// suite) never enforces declared VARCHAR(n) length at all — a SQLite-backed test can insert a
/// 500-character value into a "VARCHAR(20)" column without complaint — so this exact failure mode
/// was invisible to anything but a real Postgres connection, exactly like the transaction bug
/// BulkUpdateExecutionStrategyTests exists to catch (see that class's remarks) and for the same
/// underlying reason: MasterDataController.Import had no try/catch of any kind around the six
/// sheet-import calls, so this (or any other) exception propagated all the way out of the action
/// uncaught. The fix (this commit) adds per-row length validation ahead of every save, plus a
/// try/catch backstop for anything that validation doesn't cover — see MasterDataController's own
/// remarks. The previous commit on this branch proved the crash was genuine, in CI, against this
/// same real Postgres connection, before either fix landed.
///
/// Runs against E2E_POSTGRES_CONNECTION — the same disposable Postgres container the rest of
/// Tests.E2E already requires (see .github/workflows/_e2e-tests.yml and E2EWebAppFactory), never
/// against the shared Supabase instance. Uses its own dedicated "master_data_import_crash_test"
/// schema (built directly from the current EF model via PostgresTestSchema, not migrations) so it
/// can't collide with the "e2e_test" schema the Playwright journeys use, or with
/// "bulk_update_retry_test" (BulkUpdateExecutionStrategyTests) / "bulk_update_columns_test"
/// (BulkUpdateColumnsTests).
///
/// Joins E2ETestCollection purely for sequencing — see PostgresTestSchema's remarks and the
/// sibling BulkUpdateExecutionStrategyTests for why (concurrent CREATE EXTENSION IF NOT EXISTS
/// pg_trgm races, and EnsureCreatedAsync's non-schema-aware "already exists" check).
/// </summary>
[Collection(E2ETestCollection.Name)]
public class MasterDataImportCrashTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("E2E_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "The E2E_POSTGRES_CONNECTION environment variable is not set. Point it at a Postgres " +
            "instance this suite is allowed to create/drop a \"master_data_import_crash_test\" " +
            "schema in (e.g. a local/CI Postgres container) — same requirement as every other test " +
            "in Tests.E2E (see E2EWebAppFactory). Never point this at the shared Supabase instance " +
            "for a routine local run: this test exists specifically to exercise real Postgres " +
            "VARCHAR length enforcement, which SQLite has no equivalent of.");

    private const string SchemaName = "master_data_import_crash_test";

    private ApplicationDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var searchPath = await PostgresTestSchema.BuildSearchPathAsync(ConnectionString, SchemaName, "pg_trgm");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql($"{ConnectionString};Search Path={searchPath}", npgsql =>
            {
                // Mirrors Program.cs's ConfigureNpgsql exactly, same as BulkUpdateExecutionStrategyTests
                // — real length enforcement doesn't depend on retries being enabled, but matching
                // production's exact provider configuration keeps this test honest about what it proves.
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                npgsql.CommandTimeout(60);
            })
            .Options;

        _context = new ApplicationDbContext(options);

        await PostgresTestSchema.CreateSchemaAsync(_context, ConnectionString, SchemaName);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await PostgresTestSchema.DropSchemaAsync(ConnectionString, SchemaName);
    }

    private static MasterDataController CreateController(ApplicationDbContext context) =>
        new(context, NullLogger<MasterDataController>.Instance);

    private static IFormFile ToFormFile(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "wlm-master-data-test.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    // Builds exactly the workbook from the bug report: only "Size Groups" and "Sizes" sheets, a
    // new "HAT" size group whose Unit / Type cell ("Fitted hat size (inches)", 25 chars) overflows
    // SizeGroup.UnitType's 20-character column, and four Sizes rows referencing it.
    private static XLWorkbook BuildReportedWorkbook()
    {
        var workbook = new XLWorkbook();

        var sizeGroups = workbook.Worksheets.Add("Size Groups");
        string[] sizeGroupHeaders = { "Action", "Size Group Code", "Group Name EN", "Group Name AR", "Unit / Type", "Notes", "Active" };
        for (var i = 0; i < sizeGroupHeaders.Length; i++) sizeGroups.Cell(1, i + 1).Value = sizeGroupHeaders[i];
        sizeGroups.Cell(2, 1).Value = "ADD";
        sizeGroups.Cell(2, 2).Value = "HAT";
        sizeGroups.Cell(2, 3).Value = "Hat Size";
        sizeGroups.Cell(2, 4).Value = "مقاس القبعة";
        sizeGroups.Cell(2, 5).Value = "Fitted hat size (inches)"; // 25 chars > UnitType's [StringLength(20)]
        sizeGroups.Cell(2, 7).Value = "Yes";

        var sizes = workbook.Worksheets.Add("Sizes");
        string[] sizeHeaders = { "Action", "Size Code", "Size Group Code", "Display Name EN", "Display Name AR", "Numeric Value", "Unit", "Sort Order", "Active" };
        for (var i = 0; i < sizeHeaders.Length; i++) sizes.Cell(1, i + 1).Value = sizeHeaders[i];

        void AddSizeRow(int row, string code, string display, double numeric, int sort)
        {
            sizes.Cell(row, 1).Value = "ADD";
            sizes.Cell(row, 2).Value = code;
            sizes.Cell(row, 3).Value = "HAT";
            sizes.Cell(row, 4).Value = display;
            sizes.Cell(row, 5).Value = display;
            sizes.Cell(row, 6).Value = numeric;
            sizes.Cell(row, 7).Value = "in";
            sizes.Cell(row, 8).Value = sort;
            sizes.Cell(row, 9).Value = "Yes";
        }

        AddSizeRow(2, "HAT-7", "7", 7, 1);
        AddSizeRow(3, "HAT-714", "7 1/4", 7.25, 2);
        AddSizeRow(4, "HAT-712", "7 1/2", 7.5, 3);
        AddSizeRow(5, "HAT-734", "7 3/4", 7.75, 4);

        return workbook;
    }

    [Fact]
    public async Task Import_OversizedUnitTypeCell_ShowsRowError_DoesNotCrash()
    {
        using var workbook = BuildReportedWorkbook();
        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        // Must not throw. Against the unfixed controller, this line threw a DbUpdateException
        // (see this class's remarks, and the standalone reproduction proved in the previous
        // commit) — exactly the unhandled exception that produced the live site's generic error
        // page.
        var actionResult = await controller.Import(file);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<MasterDataImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);

        // The offending row is rejected with a specific, actionable message — not silently
        // truncated, and not swallowed into a single generic "something went wrong".
        Assert.Contains(model.Errors, e =>
            e.Contains("Size Groups row 2", StringComparison.Ordinal) &&
            e.Contains("Unit / Type", StringComparison.Ordinal) &&
            e.Contains("20", StringComparison.Ordinal));
        Assert.Equal(0, model.SizeGroupsCreated);

        // Cascading, and correct: none of the four Sizes rows can resolve "HAT" as a Size Group
        // Code, because the row that would have created it was rejected above — same as any other
        // Sizes row whose Size Group Code genuinely doesn't exist yet.
        Assert.Equal(4, model.Errors.Count(e => e.Contains("Sizes row", StringComparison.Ordinal)));
        Assert.Equal(0, model.SizesCreated);

        await using var verifyContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql($"{ConnectionString};Search Path={SchemaName}")
                .Options);
        Assert.False(await verifyContext.SizeGroups.AsNoTracking().AnyAsync(g => g.Code == "HAT"));
    }
}
