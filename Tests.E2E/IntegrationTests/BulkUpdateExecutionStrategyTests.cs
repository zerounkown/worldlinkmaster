using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.E2E.IntegrationTests;

/// <summary>
/// Regression coverage for the Bulk Update (Excel) admin tool failing every import with:
/// "The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
/// user-initiated transactions. Use the execution strategy returned by
/// 'DbContext.Database.CreateExecutionStrategy()' to execute all the operations in the
/// transaction as a retriable unit."
///
/// Program.cs enables Npgsql connection resiliency (EnableRetryOnFailure — see ConfigureNpgsql),
/// so production runs on a REAL retrying execution strategy. That strategy throws the moment
/// something calls Database.BeginTransactionAsync() directly, unconditionally — not only during
/// an actual retry — unless the call happens inside that same strategy's own ExecuteAsync
/// delegate. SQLite (used by the fast Tests/ unit-test suite) has no retrying execution strategy
/// at all, so a SQLite-backed test can prove a transaction was used correctly but can never catch
/// THIS specific failure mode — which is exactly how the previous fix (wrapping the import in a
/// plain BeginTransactionAsync/CommitAsync, PR #145) shipped with this bug undetected. This suite
/// needs a real Npgsql connection with retries enabled, matching Program.cs exactly.
///
/// Runs against E2E_POSTGRES_CONNECTION — the same disposable Postgres container the rest of
/// Tests.E2E already requires (see .github/workflows/_e2e-tests.yml and E2EWebAppFactory), never
/// against the shared Supabase instance. Uses its own dedicated "bulk_update_retry_test" schema
/// (built directly from the current EF model via EnsureCreatedAsync, not migrations) so it can't
/// collide with the "e2e_test" schema the Playwright journeys use, even when both run at once.
/// </summary>
public class BulkUpdateExecutionStrategyTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("E2E_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "The E2E_POSTGRES_CONNECTION environment variable is not set. Point it at a Postgres " +
            "instance this suite is allowed to create/drop a \"bulk_update_retry_test\" schema in " +
            "(e.g. a local/CI Postgres container) — same requirement as every other test in " +
            "Tests.E2E (see E2EWebAppFactory). Never point this at the shared Supabase instance " +
            "for a routine local run: this test exists specifically to exercise Npgsql's REAL " +
            "retrying execution strategy against real DDL/DML, which is more than a schema-scoped " +
            "no-op.");

    private const string SchemaName = "bulk_update_retry_test";

    private ApplicationDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using (var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE", admin))
            {
                await drop.ExecuteNonQueryAsync();
            }
            await using (var create = new NpgsqlCommand($"CREATE SCHEMA {SchemaName}", admin))
            {
                await create.ExecuteNonQueryAsync();
            }
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql($"{ConnectionString};Search Path={SchemaName}", npgsql =>
            {
                // Mirrors Program.cs's ConfigureNpgsql exactly — the entire point of this test is
                // exercising the REAL NpgsqlRetryingExecutionStrategy production runs under, not
                // an approximation of it.
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                npgsql.CommandTimeout(60);
            })
            .Options;

        _context = new ApplicationDbContext(options);

        // EnsureCreated (not Migrate): builds the schema straight from the current EF model into
        // this fresh, guaranteed-empty schema. Migrate() is deliberately avoided here — Npgsql's
        // migrations-history-table existence check always looks at the "public" schema regardless
        // of Search Path (see E2EWebAppFactory's ResetSchemaAsync for the full explanation), which
        // this test has no need to work around: it isn't testing that migrations apply correctly,
        // only that the import commits under a real retrying execution strategy.
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();

        await using var admin = new NpgsqlConnection(ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {SchemaName} CASCADE", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static ProductsController CreateController(ApplicationDbContext context)
    {
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, name));
        localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns((string name, object[] args) => new LocalizedString(name, string.Format(name, args)));

        var outputCache = new Mock<IOutputCacheStore>();
        outputCache.Setup(o => o.EvictByTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        return new ProductsController(context, localizer.Object, outputCache.Object, NullLogger<ProductsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static IFormFile ToFormFile(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "wlm-test.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    [Fact]
    public async Task BulkUpdate_AgainstRealNpgsqlRetryingExecutionStrategy_CommitsSuccessfully()
    {
        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        _context.Categories.Add(category);
        _context.Users.Add(user);
        _context.Merchants.Add(merchant);

        // Also throw in a pre-existing case-colliding Color pair, the other bug this endpoint had
        // (see the sibling ProductsControllerBulkUpdateTests in Tests/) — cheap to include here
        // and it proves that fix holds under real Postgres too, not just SQLite.
        _context.Colors.Add(new Color { Name = "Black", HexCode = "#1c1c1c" });
        _context.Colors.Add(new Color { Name = "black", HexCode = "#1c1c1c" });
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        var variant = new ProductVariant { ProductId = product.Id, Sku = "1524003", StockQuantity = 0 };
        _context.ProductVariants.Add(variant);
        await _context.SaveChangesAsync();

        using var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders = { "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];
        productsSheet.Cell(2, 1).Value = "152";
        productsSheet.Cell(2, 2).Value = "Field Pants";
        productsSheet.Cell(2, 3).Value = "Tactical Apparel";
        productsSheet.Cell(2, 4).Value = 100m;
        productsSheet.Cell(2, 6).Value = 50;

        var variantsSheet = workbook.Worksheets.Add("Variants");
        string[] variantHeaders = { "Product Sku (Parent)", "Variant Sku", "Color", "Size", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
        for (var i = 0; i < variantHeaders.Length; i++) variantsSheet.Cell(1, i + 1).Value = variantHeaders[i];
        variantsSheet.Cell(2, 1).Value = "152";
        variantsSheet.Cell(2, 2).Value = "1524003";
        variantsSheet.Cell(2, 7).Value = 1; // the one-cell stock change from the original bug report

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        // Must not throw, and specifically must not produce the "does not support user-initiated
        // transactions" FatalError — that's the exact failure this test exists to catch.
        var actionResult = await controller.BulkUpdate(file);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.UpdatedCount);
        Assert.Equal(1, model.VariantsUpdatedCount);

        // Confirms the transaction genuinely committed against the real database, via a brand-new
        // context/connection rather than the tracked entity this test already holds.
        await using var verifyContext = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql($"{ConnectionString};Search Path={SchemaName}")
                .Options);
        var reloadedVariant = await verifyContext.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "1524003");
        Assert.Equal(1, reloadedVariant.StockQuantity);
    }
}
