using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// Regression coverage for the Bulk Update (Excel) admin tool crashing with a generic 500 on
/// the live site. Root cause: ImportVariantsSheet built its Color/Size lookups with a plain
/// `.ToDictionary(..., StringComparer.OrdinalIgnoreCase)` over ALL existing Colors/Sizes —
/// unconditionally, before reading a single row. Color.Name and Size.Label have no uniqueness
/// constraint in the database (only their Code does), so any pre-existing pair that collides
/// case-insensitively (e.g. "Black"/"black", or "30x32"/"30X32") throws "An item with the same
/// key has already been added" and takes the whole request down — even on a re-upload of an
/// unmodified export where every row is a plain update. Uses a real (SQLite) relational
/// provider, not EF's InMemory provider, because the fix wraps the import in a database
/// transaction (BeginTransactionAsync), which InMemory doesn't support.
/// </summary>
public class ProductsControllerBulkUpdateTests
{
    private static (ApplicationDbContext Context, SqliteConnection Connection) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
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

    private static int ColumnOf(IXLWorksheet sheet, string header)
    {
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            if (cell.GetString().Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                return cell.Address.ColumnNumber;
            }
        }
        throw new InvalidOperationException($"Header '{header}' not found.");
    }

    private static (Category Category, Merchant Merchant) SeedCategoryAndMerchant(ApplicationDbContext context)
    {
        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        // Merchant.UserId is a real FK to AspNetUsers (see ApplicationDbContext), enforced by
        // SQLite (unlike EF's InMemory provider) — the backing user row has to exist too.
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        context.Categories.Add(category);
        context.Users.Add(user);
        context.Merchants.Add(merchant);
        context.SaveChanges();
        return (category, merchant);
    }

    // The user's actual report: download the catalog, change exactly one variant's Stock
    // Quantity, re-upload — every other cell is byte-for-byte what the export wrote. Exercises
    // the real ExportExcel() writer (not a hand-built workbook), so a future header/column
    // rename that broke the round trip would fail here too.
    [Fact]
    public async Task BulkUpdate_ExportThenReimportWithOneStockChange_RoundTripsSuccessfully()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var color = new Color { Name = "Coyote Tan", HexCode = "#b08d57" };
        var size = new Size { Label = "30x32" };
        context.Colors.Add(color);
        context.Sizes.Add(size);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        var variant = new ProductVariant { ProductId = product.Id, ColorId = color.Id, SizeId = size.Id, Sku = "1524003", StockQuantity = 0 };
        context.ProductVariants.Add(variant);
        context.SaveChanges();

        var controller = CreateController(context);

        // Step 1: download, exactly like the admin page's "Download Products.xlsx" link.
        var exportResult = await controller.ExportExcel();
        var fileResult = Assert.IsType<FileContentResult>(exportResult);
        using var exported = new XLWorkbook(new MemoryStream(fileResult.FileContents));

        // Step 2: the ONE change described in the bug report — Variant Sku 1524003's Stock
        // Quantity, 0 -> 1. Nothing else in the file is touched.
        var variantsSheet = exported.Worksheet("Variants");
        var skuCol = ColumnOf(variantsSheet, "Variant Sku");
        var stockCol = ColumnOf(variantsSheet, "Stock Quantity");
        var targetRow = variantsSheet.RowsUsed().Skip(1).First(r => r.Cell(skuCol).GetString().Trim() == "1524003");
        targetRow.Cell(stockCol).Value = 1;

        var file = ToFormFile(exported);

        // Step 3: re-upload.
        var importResult = await controller.BulkUpdate(file);

        var viewResult = Assert.IsType<ViewResult>(importResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.UpdatedCount); // the Products-sheet row for SKU 152 (values unchanged, still counted)
        Assert.Equal(1, model.VariantsUpdatedCount);
        Assert.Equal(0, model.VariantsCreatedCount);

        var reloadedVariant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "1524003");
        Assert.Equal(1, reloadedVariant.StockQuantity);
    }

    // Regression test for the actual crash: pre-existing Color/Size rows that collide once case
    // is ignored (a known, unenforced data-quality gap — Color.Name/Size.Label have no unique
    // index) must not blow up an import whose rows don't even touch those duplicates. Before the
    // fix, ImportVariantsSheet's `.ToDictionary(..., StringComparer.OrdinalIgnoreCase)` over
    // *all* Colors/Sizes threw ArgumentException here, unconditionally, before a single Variants
    // row was read.
    [Fact]
    public async Task BulkUpdate_PreExistingDuplicateColorAndSizeNames_DoesNotCrash()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);

        // Two colors and two sizes already in the database, each pair colliding only once case
        // is ignored — exactly the shape flagged as a live data-quality issue.
        context.Colors.Add(new Color { Name = "Black", HexCode = "#1c1c1c" });
        context.Colors.Add(new Color { Name = "black", HexCode = "#1c1c1c" });
        context.Sizes.Add(new Size { Label = "30x32" });
        context.Sizes.Add(new Size { Label = "30X32" });

        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        var variant = new ProductVariant { ProductId = product.Id, Sku = "1524003", StockQuantity = 0 };
        context.ProductVariants.Add(variant);
        context.SaveChanges();

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
        variantsSheet.Cell(2, 7).Value = 1; // the one-cell stock change from the bug report

        var file = ToFormFile(workbook);
        var controller = CreateController(context);

        // Must not throw — this is the regression this test guards against.
        var actionResult = await controller.BulkUpdate(file);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsUpdatedCount);

        var reloadedVariant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "1524003");
        Assert.Equal(1, reloadedVariant.StockQuantity);

        // The pre-existing duplicates weren't touched or added to.
        Assert.Equal(2, await context.Colors.CountAsync());
        Assert.Equal(2, await context.Sizes.CountAsync());
    }

    // Tolerant size matching (never mint a near-duplicate size over punctuation/case): a new
    // variant whose Size cell uses "×" (multiplication sign) must resolve to the SAME existing
    // Size row as one stored with an ASCII "x", not create a second Size that differs only by
    // separator character.
    [Fact]
    public async Task BulkUpdate_NewVariantSizeUsesDifferentSeparator_ReusesExistingSizeInsteadOfDuplicating()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var existingSize = new Size { Label = "30x32" };
        context.Sizes.Add(existingSize);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

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
        variantsSheet.Cell(2, 2).Value = "152-NEW-3032";
        variantsSheet.Cell(2, 4).Value = "30×32"; // "30×32" — multiplication sign, not "x"
        variantsSheet.Cell(2, 7).Value = 5;

        var file = ToFormFile(workbook);
        var controller = CreateController(context);

        var actionResult = await controller.BulkUpdate(file);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsCreatedCount);

        // Only the one, pre-existing Size row exists — no "30×32" duplicate was minted.
        Assert.Equal(1, await context.Sizes.CountAsync());
        var newVariant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-NEW-3032");
        Assert.Equal(existingSize.Id, newVariant.SizeId);
    }
}
