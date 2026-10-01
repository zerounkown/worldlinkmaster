using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

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

        var storefrontCache = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        return new ProductsController(context, localizer.Object, outputCache.Object, NullLogger<ProductsController>.Instance, storefrontCache)
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

    private static XLWorkbook NewWorkbookWithHeaders()
    {
        var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders =
        {
            "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL",
            "Name (Arabic)", "Brand", "Subcategory", "Size Group", "Published"
        };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];

        var variantsSheet = workbook.Worksheets.Add("Variants");
        string[] variantHeaders =
        {
            "Product Sku (Parent)", "Internal Barcode", "Variant Sku", "Color", "Size", "Length",
            "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Stock Quantity", "Image URL"
        };
        for (var i = 0; i < variantHeaders.Length; i++) variantsSheet.Cell(1, i + 1).Value = variantHeaders[i];

        return workbook;
    }

    // Leaves a cell truly blank when the corresponding argument is null — matching how ClosedXML
    // represents an untouched cell (IsEmpty() == true), not an empty string.
    private static void WriteProductRow(
        IXLWorksheet sheet, int row, string sku, string name, string category, decimal price, int stock,
        decimal? wholesale = null, string? nameAr = null, string? brand = null, string? subcategory = null,
        string? sizeGroup = null, string? published = null)
    {
        sheet.Cell(row, 1).Value = sku;
        sheet.Cell(row, 2).Value = name;
        sheet.Cell(row, 3).Value = category;
        sheet.Cell(row, 4).Value = price;
        if (wholesale.HasValue) sheet.Cell(row, 5).Value = wholesale.Value;
        sheet.Cell(row, 6).Value = stock;
        if (nameAr != null) sheet.Cell(row, 8).Value = nameAr;
        if (brand != null) sheet.Cell(row, 9).Value = brand;
        if (subcategory != null) sheet.Cell(row, 10).Value = subcategory;
        if (sizeGroup != null) sheet.Cell(row, 11).Value = sizeGroup;
        if (published != null) sheet.Cell(row, 12).Value = published;
    }

    private static void WriteVariantRow(
        IXLWorksheet sheet, int row, string parentSku, string variantSku, int stock,
        string? barcode = null, string? color = null, string? size = null, string? length = null,
        decimal? priceExclVat = null, decimal? priceInclVat = null, decimal? wholesale = null)
    {
        sheet.Cell(row, 1).Value = parentSku;
        if (barcode != null) sheet.Cell(row, 2).Value = barcode;
        sheet.Cell(row, 3).Value = variantSku;
        if (color != null) sheet.Cell(row, 4).Value = color;
        if (size != null) sheet.Cell(row, 5).Value = size;
        if (length != null) sheet.Cell(row, 6).Value = length;
        if (priceExclVat.HasValue) sheet.Cell(row, 7).Value = priceExclVat.Value;
        if (priceInclVat.HasValue) sheet.Cell(row, 8).Value = priceInclVat.Value;
        if (wholesale.HasValue) sheet.Cell(row, 9).Value = wholesale.Value;
        sheet.Cell(row, 10).Value = stock;
    }

    // Fast SQLite-backed smoke test for the supplier-catalog columns/rules (see the Postgres-
    // backed BulkUpdateColumnsTests in Tests.E2E for the full, real-database-constraint-backed
    // coverage — Barcode's uniqueness is enforced by a real Postgres unique index that SQLite
    // can't be relied on to validate identically).
    [Fact]
    public async Task BulkUpdate_BlankCellsLeaveExistingValuesUnchanged_NonBlankCellsApply()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var brand = new Brand { Name = "Condor", Slug = "condor" };
        context.Brands.Add(brand);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, WholesalePrice = 25m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id, IsPublished = true
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        // Wholesale Price, Subcategory, Size Group, and Published are all left blank — none of
        // them should change. Name (Arabic) and Brand ARE provided and should apply.
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 110m, 999,
            nameAr: "بنطال ميداني", brand: "Condor");

        var controller = CreateController(context);
        var actionResult = await controller.BulkUpdate(ToFormFile(workbook));
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(actionResult).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(110m, reloaded.Price);
        Assert.Equal(25m, reloaded.WholesalePrice); // untouched — blank cell
        Assert.Equal("بنطال ميداني", reloaded.NameAr);
        Assert.Equal(brand.Id, reloaded.BrandId);
        Assert.Null(reloaded.SubcategoryId);
        Assert.True(reloaded.IsPublished);
        Assert.Equal(999, reloaded.StockQuantity); // no variants on this product — cell value applies
    }

    [Fact]
    public async Task BulkUpdate_UnrecognizedBrand_FailsThatRowWithoutTouchingTheProduct()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 200m, 999, brand: "NoSuchBrand");

        var controller = CreateController(context);
        var actionResult = await controller.BulkUpdate(ToFormFile(workbook));
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(actionResult).Model);

        Assert.Null(model.FatalError);
        Assert.Equal(0, model.UpdatedCount);
        Assert.Single(model.Errors);
        Assert.Contains("unrecognized Brand", model.Errors[0]);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(100m, reloaded.Price); // the whole row was skipped, not just the Brand part
        Assert.Equal(0, await context.Brands.CountAsync()); // never auto-created
    }

    [Fact]
    public async Task BulkUpdate_DuplicateBarcode_FailsThatRowWithoutCrashing()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        var variantA = new ProductVariant { ProductId = product.Id, Sku = "152-A", StockQuantity = 1, Barcode = "1111111111" };
        var variantB = new ProductVariant { ProductId = product.Id, Sku = "152-B", StockQuantity = 2, Barcode = null };
        context.ProductVariants.AddRange(variantA, variantB);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        // Try to give variant B the barcode variant A already has.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-B", 2, barcode: "1111111111");

        var controller = CreateController(context);
        var actionResult = await controller.BulkUpdate(ToFormFile(workbook));
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(actionResult).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("already belongs to variant '152-A'", model.Errors[0]);

        var reloadedB = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-B");
        Assert.Null(reloadedB.Barcode);
    }

    [Fact]
    public async Task BulkUpdate_ProductWithVariants_StockQuantityIsAlwaysTheSumOfVariantStock()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 999, // deliberately stale — must get overridden
            CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        // Neither variant is mentioned in the uploaded file below.
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, Sku = "152-A", StockQuantity = 3 },
            new ProductVariant { ProductId = product.Id, Sku = "152-B", StockQuantity = 4 });
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        // Stock Quantity cell says 50 — must be ignored in favor of the variant sum (7).
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 50);

        var controller = CreateController(context);
        var actionResult = await controller.BulkUpdate(ToFormFile(workbook));
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(actionResult).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(7, reloaded.StockQuantity);
    }

    [Fact]
    public async Task BulkUpdate_NewProduct_DefaultsToUnpublished_UnlessPublishedColumnSaysYes()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "NEW-001", "Unpublished By Default", "Tactical Apparel", 50m, 10); // Published left blank
        WriteProductRow(productsSheet, 3, "NEW-002", "Published Explicitly", "Tactical Apparel", 60m, 20, published: "Yes");

        var controller = CreateController(context);
        var actionResult = await controller.BulkUpdate(ToFormFile(workbook));
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(actionResult).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(2, model.CreatedCount);

        Assert.False((await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-001")).IsPublished);
        Assert.True((await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-002")).IsPublished);
    }

    // --- Variant Price (AED) / Price+VAT --------------------------------------------------
    // The site stores prices INCLUDING 5% VAT (ProductVariant.Price). The supplier reference
    // format gives both the excl.-VAT price and, optionally, the already-VAT-inclusive price —
    // TryResolveVariantPrice reconciles the two into that one stored value.

    [Fact]
    public async Task BulkUpdate_VariantBothPriceColumns_Agreeing_SavesThePriceInclVatValue()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        // 100 excl. VAT x 1.05 = 105.00 — matches the given Price+VAT exactly.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-V1", 5, priceExclVat: 100m, priceInclVat: 105m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsCreatedCount);
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-V1");
        Assert.Equal(105.00m, variant.Price);
    }

    [Fact]
    public async Task BulkUpdate_VariantOnlyExclVatProvided_ComputesInclVatAt105Percent()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-V2", 5, priceExclVat: 200m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-V2");
        Assert.Equal(210.00m, variant.Price); // 200 * 1.05
    }

    [Fact]
    public async Task BulkUpdate_VariantOnlyInclVatProvided_UsesItDirectly()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-V3", 5, priceInclVat: 150m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-V3");
        Assert.Equal(150.00m, variant.Price);
    }

    [Fact]
    public async Task BulkUpdate_VariantPriceColumnsDisagree_FailsTheRowAndSavesNothing()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        // 100 x 1.05 = 105.00, but Price+VAT says 999 — well outside the 0.01 tolerance.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-V4", 5, priceExclVat: 100m, priceInclVat: 999m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("doesn't match", model.Errors[0]);
        Assert.Equal(0, model.VariantsCreatedCount);
        Assert.False(await context.ProductVariants.AnyAsync(v => v.Sku == "152-V4"));
    }

    [Fact]
    public async Task BulkUpdate_OldFormatFile_NoPriceVatColumnAtAll_TreatsPriceAedAsAlreadyVatInclusive()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        // The OLD 8-column shape — no Internal Barcode, no Length, and critically no Price+VAT —
        // where "Price (AED)" meant the VAT-INCLUSIVE price directly.
        using var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders = { "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];
        productsSheet.Cell(2, 1).Value = "152";
        productsSheet.Cell(2, 2).Value = "Field Pants";
        productsSheet.Cell(2, 3).Value = "Tactical Apparel";
        productsSheet.Cell(2, 4).Value = 100m;
        productsSheet.Cell(2, 6).Value = 0;

        var variantsSheet = workbook.Worksheets.Add("Variants");
        string[] variantHeaders = { "Product Sku (Parent)", "Variant Sku", "Color", "Size", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
        for (var i = 0; i < variantHeaders.Length; i++) variantsSheet.Cell(1, i + 1).Value = variantHeaders[i];
        variantsSheet.Cell(2, 1).Value = "152";
        variantsSheet.Cell(2, 2).Value = "152-OLD";
        variantsSheet.Cell(2, 5).Value = 105m; // old semantics: already VAT-inclusive
        variantsSheet.Cell(2, 7).Value = 5;

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-OLD");
        // Must NOT be 105 x 1.05 = 110.25 — that would be double-applying VAT to an
        // already-VAT-inclusive value.
        Assert.Equal(105.00m, variant.Price);
    }

    // --- Variant Size + Length ---------------------------------------------------------------

    [Fact]
    public async Task BulkUpdate_NewVariantWithSizeAndLength_CombinesToMatchExistingSize_NeverDuplicates()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var existingSize = new Size { Label = "28x30" };
        context.Sizes.Add(existingSize);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-SL1", 3, size: "28", length: "30");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, await context.Sizes.CountAsync()); // no duplicate "28x30" minted
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-SL1");
        Assert.Equal(existingSize.Id, variant.SizeId);
    }

    [Fact]
    public async Task BulkUpdate_NewVariantWithSizeOnly_LengthBlank_SizeIsJustTheSizeValue()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        // A letter size (jackets) and a bare shoe size — neither has a Length.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-SL2", 3, size: "M");
        WriteVariantRow(workbook.Worksheet("Variants"), 3, "152", "152-SL3", 4, size: "42");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var variantM = await context.ProductVariants.AsNoTracking().Include(v => v.Size).FirstAsync(v => v.Sku == "152-SL2");
        var variant42 = await context.ProductVariants.AsNoTracking().Include(v => v.Size).FirstAsync(v => v.Sku == "152-SL3");
        Assert.Equal("M", variantM.Size!.Label);
        Assert.Equal("42", variant42.Size!.Label);
    }

    [Fact]
    public async Task ExportExcel_SplitsCombinedSizeIntoSizeAndLength_LeavesLengthBlankForNonCombinedSizes()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var combinedSize = new Size { Label = "28x30" };
        var letterSize = new Size { Label = "M" };
        context.Sizes.AddRange(combinedSize, letterSize);
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        context.Products.Add(product);
        context.SaveChanges();
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, SizeId = combinedSize.Id, Sku = "152-EXP1", StockQuantity = 1 },
            new ProductVariant { ProductId = product.Id, SizeId = letterSize.Id, Sku = "152-EXP2", StockQuantity = 1 });
        context.SaveChanges();

        var controller = CreateController(context);
        var fileResult = Assert.IsType<FileContentResult>(await controller.ExportExcel());
        using var exported = new XLWorkbook(new MemoryStream(fileResult.FileContents));
        var variantsSheet = exported.Worksheet("Variants");
        var skuCol = ColumnOf(variantsSheet, "Variant Sku");
        var sizeCol = ColumnOf(variantsSheet, "Size");
        var lengthCol = ColumnOf(variantsSheet, "Length");

        var combinedRow = variantsSheet.RowsUsed().Skip(1).First(r => r.Cell(skuCol).GetString().Trim() == "152-EXP1");
        Assert.Equal("28", combinedRow.Cell(sizeCol).GetString().Trim());
        Assert.Equal("30", combinedRow.Cell(lengthCol).GetString().Trim());

        var letterRow = variantsSheet.RowsUsed().Skip(1).First(r => r.Cell(skuCol).GetString().Trim() == "152-EXP2");
        Assert.Equal("M", letterRow.Cell(sizeCol).GetString().Trim());
        Assert.True(letterRow.Cell(lengthCol).IsEmpty());
    }

    // --- Products sheet Price (AED) / Price+VAT -----------------------------------------------
    // Both sheets used to disagree about what a bare "Price (AED)" column meant — excl. VAT on
    // Variants, incl. VAT on Products — which was genuinely dangerous: a new product's Price
    // (AED) cell, filled in with the excl.-VAT figure out of habit from the Variants sheet, would
    // have silently become the site's stored (and charged) price. These use their OWN workbook
    // helper (NewWorkbookWithVatPricingHeaders/WriteProductRowVat) with the current, full header
    // shape (Price+VAT present) — deliberately separate from NewWorkbookWithHeaders()/
    // WriteProductRow() above, which stay on the older single-price-column shape so the many
    // existing tests using them keep exercising the backward-compatible "old format" path
    // unchanged, exactly as they did before this column was added.

    private static XLWorkbook NewWorkbookWithVatPricingHeaders()
    {
        var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders =
        {
            "Sku", "Name", "Category", "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Wholesale Price+VAT",
            "Stock Quantity", "Image URL", "Name (Arabic)", "Brand", "Subcategory", "Size Group", "Published"
        };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];

        var variantsSheet = workbook.Worksheets.Add("Variants");
        string[] variantHeaders =
        {
            "Product Sku (Parent)", "Internal Barcode", "Variant Sku", "Color", "Size", "Length",
            "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Wholesale Price+VAT", "Stock Quantity", "Image URL"
        };
        for (var i = 0; i < variantHeaders.Length; i++) variantsSheet.Cell(1, i + 1).Value = variantHeaders[i];

        return workbook;
    }

    private static void WriteProductRowVat(
        IXLWorksheet sheet, int row, string sku, string name, string category, int stock,
        decimal? priceExclVat = null, decimal? priceInclVat = null,
        decimal? wholesaleExclVat = null, decimal? wholesaleInclVat = null)
    {
        sheet.Cell(row, 1).Value = sku;
        sheet.Cell(row, 2).Value = name;
        sheet.Cell(row, 3).Value = category;
        if (priceExclVat.HasValue) sheet.Cell(row, 4).Value = priceExclVat.Value;
        if (priceInclVat.HasValue) sheet.Cell(row, 5).Value = priceInclVat.Value;
        if (wholesaleExclVat.HasValue) sheet.Cell(row, 6).Value = wholesaleExclVat.Value;
        if (wholesaleInclVat.HasValue) sheet.Cell(row, 7).Value = wholesaleInclVat.Value;
        sheet.Cell(row, 8).Value = stock;
    }

    [Fact]
    public async Task BulkUpdate_NewProductBothPriceColumnsAgreeing_SavesThePriceInclVatValue()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithVatPricingHeaders();
        // 100 excl. VAT x 1.05 = 105.00 — matches Price+VAT exactly.
        WriteProductRowVat(workbook.Worksheet("Products"), 2, "NEW-VAT1", "New Product", "Tactical Apparel", 10, priceExclVat: 100m, priceInclVat: 105m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.CreatedCount);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-VAT1");
        Assert.Equal(105.00m, product.Price);
    }

    [Fact]
    public async Task BulkUpdate_NewProductOnlyExclVatProvided_ComputesInclVatAt105Percent()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithVatPricingHeaders();
        WriteProductRowVat(workbook.Worksheet("Products"), 2, "NEW-VAT2", "New Product", "Tactical Apparel", 10, priceExclVat: 200m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-VAT2");
        Assert.Equal(210.00m, product.Price); // 200 * 1.05
    }

    [Fact]
    public async Task BulkUpdate_ProductPriceColumnsDisagree_FailsTheRowAndSavesNothing()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithVatPricingHeaders();
        // 100 x 1.05 = 105.00, but Price+VAT says 999 — well outside the 0.01 tolerance.
        WriteProductRowVat(workbook.Worksheet("Products"), 2, "NEW-VAT3", "New Product", "Tactical Apparel", 10, priceExclVat: 100m, priceInclVat: 999m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("doesn't match", model.Errors[0]);
        Assert.Equal(0, model.CreatedCount);
        Assert.False(await context.Products.AnyAsync(p => p.Sku == "NEW-VAT3"));
    }

    [Fact]
    public async Task BulkUpdate_ProductWholesalePriceColumns_ResolveIndependentlyOfPrice()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithVatPricingHeaders();
        // Price: only incl. VAT given. Wholesale: only excl. VAT given (50 x 1.05 = 52.50).
        WriteProductRowVat(workbook.Worksheet("Products"), 2, "NEW-VAT4", "New Product", "Tactical Apparel", 10, priceInclVat: 150m, wholesaleExclVat: 50m);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-VAT4");
        Assert.Equal(150.00m, product.Price);
        Assert.Equal(52.50m, product.WholesalePrice);
    }

    [Fact]
    public async Task BulkUpdate_OldFormatProductsSheet_NoPriceVatColumnAtAll_TreatsPriceAedAsAlreadyVatInclusive()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        // The OLD shape — no "Price+VAT" column at all — where "Price (AED)" meant the
        // VAT-INCLUSIVE price directly.
        using var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders = { "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];
        productsSheet.Cell(2, 1).Value = "NEW-VAT5";
        productsSheet.Cell(2, 2).Value = "Old Format Product";
        productsSheet.Cell(2, 3).Value = "Tactical Apparel";
        productsSheet.Cell(2, 4).Value = 105m; // old semantics: already VAT-inclusive
        productsSheet.Cell(2, 6).Value = 10;

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-VAT5");
        // Must NOT be 105 x 1.05 = 110.25 — that would be double-applying VAT.
        Assert.Equal(105.00m, product.Price);
    }

    // --- ProductColor backfill -----------------------------------------------------------
    // Bug report: product F5519 "Tactical Boonie" (created via Bulk Update, 8 variants across
    // Black/Khaki) has no "Vendor Color Codes" table on its Edit page at all — Bulk Update's
    // ImportVariantsSheet only ever sets ProductVariant.ColorId, never creates the ProductColor
    // join row that table (and Details.cshtml's per-color gallery/SKU/price lookups) requires.
    // See ProductsController.BackfillProductColorsFromVariants.

    [Fact]
    public async Task BulkUpdate_NewProductWithColorVariants_CreatesProductColorRowsAndLinksVariants()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 40m, 0);
        var variantsSheet = workbook.Worksheet("Variants");
        WriteVariantRow(variantsSheet, 2, "F5519", "F5519-BLK-S", 5, color: "Black", size: "S");
        WriteVariantRow(variantsSheet, 3, "F5519", "F5519-BLK-M", 5, color: "Black", size: "M");
        WriteVariantRow(variantsSheet, 4, "F5519", "F5519-KHK-S", 5, color: "Khaki", size: "S");
        WriteVariantRow(variantsSheet, 5, "F5519", "F5519-KHK-M", 5, color: "Khaki", size: "M");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.CreatedCount);
        Assert.Equal(4, model.VariantsCreatedCount);
        Assert.Equal(2, model.ProductColorsCreated); // one per distinct color, not per variant

        var product = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "F5519");
        var productColors = await context.ProductColors.AsNoTracking().Include(pc => pc.Color)
            .Where(pc => pc.ProductId == product.Id).ToListAsync();
        Assert.Equal(2, productColors.Count);
        Assert.Contains(productColors, pc => pc.Color!.Name == "Black");
        Assert.Contains(productColors, pc => pc.Color!.Name == "Khaki");
        // Globally-unique Code, generated from the product's own SKU — never left blank (Required).
        Assert.All(productColors, pc => Assert.StartsWith("F5519-", pc.Code));
        Assert.Equal(productColors.Select(pc => pc.Code).Distinct().Count(), productColors.Count);
        // Exactly one default, since the product started with none.
        Assert.Single(productColors, pc => pc.DefaultColor);

        // Every variant's ProductColorId links to the matching color's row — required so
        // Details.cshtml's ProductColorId-keyed SKU/price lookups work once ANY ProductColor
        // exists for this product (see BackfillProductColorsFromVariants's remarks).
        var variants = await context.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id).ToListAsync();
        Assert.Equal(4, variants.Count);
        Assert.All(variants, v =>
        {
            Assert.NotNull(v.ProductColorId);
            var owningColor = productColors.First(pc => pc.Id == v.ProductColorId);
            Assert.Equal(v.ColorId, owningColor.ColorId);
        });
    }

    // The whole-catalog side effect this needs to work at all: re-running Bulk Update with a file
    // that never mentions "24-7 Agility Pant" must still backfill ITS ProductColor rows, the same
    // way it already backfills stock sums for every product system-wide (ImportVariantsSheet's
    // existingVariants query loads the WHOLE ProductVariants table, not just matched rows) — this
    // is what satisfies "or re-running Bulk Update" as an alternative to visiting the Edit page.
    [Fact]
    public async Task BulkUpdate_FileDoesNotMentionProduct_StillBackfillsThatProductsColorsToo()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        // Mirrors "24-7 Agility Pant": seeded directly (not via Bulk Update), 7 distinct colors,
        // Ranger Green priced differently from the rest, zero ProductColor rows — exactly the
        // shape Data/SeedData.cs produces, and exactly what the task asks to confirm isn't broken.
        var black = new Color { Name = "Black", HexCode = "#1c1c1c" };
        var coyoteTan = new Color { Name = "Coyote Tan", HexCode = "#b08d57" };
        var rangerGreen = new Color { Name = "Ranger Green", HexCode = "#4b5320" };
        var navy = new Color { Name = "Navy", HexCode = "#1b263b" };
        var khaki = new Color { Name = "Khaki", HexCode = "#c3b091" };
        var slateGray = new Color { Name = "Slate Gray", HexCode = "#6c757d" };
        var arcticWhite = new Color { Name = "Arctic White", HexCode = "#eef1ee" };
        context.Colors.AddRange(black, coyoteTan, rangerGreen, navy, khaki, slateGray, arcticWhite);
        var agilityPant = new Product
        {
            Sku = "WLM-APP-006", Name = "24-7 Agility Pant", Slug = "24-7-agility-pant", Price = 530.25m,
            StockQuantity = 90, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(agilityPant);
        context.SaveChanges();
        foreach (var color in new[] { black, coyoteTan, rangerGreen, navy, khaki, slateGray, arcticWhite })
        {
            context.ProductVariants.Add(new ProductVariant
            {
                ProductId = agilityPant.Id,
                ColorId = color.Id,
                Sku = $"WLM-APP-006-{color.Name.Replace(" ", "")}",
                StockQuantity = 12,
                Price = color == rangerGreen ? 472.50m : null // untouched base price for every other color
            });
        }
        context.SaveChanges();

        // An unrelated new product — the uploaded file's only row — still needs a Variants sheet
        // present so ImportVariantsSheet (and its whole-table load) actually runs.
        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "UNRELATED-001", "Unrelated Product", "Tactical Apparel", 20m, 5);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(7, model.ProductColorsCreated);

        var productColors = await context.ProductColors.AsNoTracking().Include(pc => pc.Color)
            .Where(pc => pc.ProductId == agilityPant.Id).ToListAsync();
        Assert.Equal(7, productColors.Count);
        Assert.Contains(productColors, pc => pc.Color!.Name == "Ranger Green");

        // Nothing about the variants themselves changed beyond gaining a ProductColorId — Ranger
        // Green's price override survives untouched, and the product's own base Price is untouched.
        var variants = await context.ProductVariants.AsNoTracking().Where(v => v.ProductId == agilityPant.Id).ToListAsync();
        Assert.All(variants, v => Assert.NotNull(v.ProductColorId));
        var rangerGreenVariant = variants.First(v => v.ColorId == rangerGreen.Id);
        Assert.Equal(472.50m, rangerGreenVariant.Price);
        var reloadedProduct = await context.Products.AsNoTracking().FirstAsync(p => p.Id == agilityPant.Id);
        Assert.Equal(530.25m, reloadedProduct.Price);

        // Each variant's ProductColorId points at the row for ITS OWN color, not a mismatched one.
        foreach (var variant in variants)
        {
            var owningColor = productColors.First(pc => pc.Id == variant.ProductColorId);
            Assert.Equal(variant.ColorId, owningColor.ColorId);
        }
    }

    [Fact]
    public async Task BulkUpdate_ProductAlreadyHasSomeProductColors_OnlyBackfillsTheMissingColor_KeepsExistingDefault()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var black = new Color { Name = "Black", HexCode = "#1c1c1c" };
        var khaki = new Color { Name = "Khaki", HexCode = "#c3b091" };
        context.Colors.AddRange(black, khaki);
        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 10, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        // Black already has a real ProductColor row (e.g. from an earlier Product Importer run)
        // and is already the default — that must survive untouched.
        var existingBlackProductColor = new ProductColor { Code = "F5519-BLACK-EXISTING", ProductId = product.Id, ColorId = black.Id, DefaultColor = true, DisplayOrder = 0 };
        context.ProductColors.Add(existingBlackProductColor);
        context.SaveChanges(); // must commit before referencing .Id below — it's still 0 until this runs
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, ColorId = black.Id, ProductColorId = existingBlackProductColor.Id, Sku = "F5519-BLK", StockQuantity = 5 },
            new ProductVariant { ProductId = product.Id, ColorId = khaki.Id, Sku = "F5519-KHK", StockQuantity = 5 }); // Khaki never got its ProductColor row
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 40m, 10);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Equal(1, model.ProductColorsCreated); // only Khaki

        var productColors = await context.ProductColors.AsNoTracking().Include(pc => pc.Color)
            .Where(pc => pc.ProductId == product.Id).ToListAsync();
        Assert.Equal(2, productColors.Count);
        var reloadedBlack = productColors.First(pc => pc.ColorId == black.Id);
        Assert.Equal("F5519-BLACK-EXISTING", reloadedBlack.Code); // untouched, not recreated
        Assert.True(reloadedBlack.DefaultColor);
        var newKhaki = productColors.First(pc => pc.ColorId == khaki.Id);
        Assert.False(newKhaki.DefaultColor); // Black already had the default — Khaki doesn't steal it
    }

    // --- Size Group-scoped Size matching -----------------------------------------------
    // Bug report: product F5519 "Tactical Boonie" (Size Group "Hat Size", code HAT) showed sizes
    // in the wrong order (7 1/4, 7 1/2, 7 3/4, 7 instead of 7, 7 1/4, 7 1/2, 7 3/4) even though
    // Master Data created them with the correct Sort Order (1-4). Root cause: ImportVariantsSheet
    // matched a variant's Size by label across ALL Sizes in the database, completely ignoring
    // Size Group — "7" (a short, common label) matched an unrelated pre-existing Size from a
    // different group (e.g. a footwear size), and Views/Products/Details.cshtml orders a
    // product's sizes by Size.SortOrder, so the variant displayed in THAT group's position, not
    // Hat Size's. See ProductsController.ImportVariantsSheet's local ResolveSize function.

    [Fact]
    public async Task BulkUpdate_ProductWithSizeGroup_NewVariantMatchesSizeWithinGroup_NotACollidingLabelInAnotherGroup()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var hatSizeGroup = new SizeGroup { Code = "HAT", NameEn = "Hat Size" };
        var footwearGroup = new SizeGroup { Code = "SHOE", NameEn = "Footwear" };
        context.SizeGroups.AddRange(hatSizeGroup, footwearGroup);
        context.SaveChanges();
        // Master Data already created the Hat Size sizes with the real Sort Order (1-4) reported
        // in the bug — and a completely unrelated "7" already exists in Footwear with a much
        // higher Sort Order, which is exactly what a naive global-label match would collide with.
        var hat7 = new Size { Code = "HAT-7", Label = "7", SizeGroupId = hatSizeGroup.Id, SortOrder = 1 };
        var hat714 = new Size { Code = "HAT-714", Label = "7 1/4", SizeGroupId = hatSizeGroup.Id, SortOrder = 2 };
        var hat712 = new Size { Code = "HAT-712", Label = "7 1/2", SizeGroupId = hatSizeGroup.Id, SortOrder = 3 };
        var hat734 = new Size { Code = "HAT-734", Label = "7 3/4", SizeGroupId = hatSizeGroup.Id, SortOrder = 4 };
        var shoe7 = new Size { Code = "SHOE-7", Label = "7", SizeGroupId = footwearGroup.Id, SortOrder = 20 };
        context.Sizes.AddRange(hat7, hat714, hat712, hat734, shoe7);
        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = hatSizeGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 40m, 0);
        var variantsSheet = workbook.Worksheet("Variants");
        WriteVariantRow(variantsSheet, 2, "F5519", "F5519-7", 5, size: "7");
        WriteVariantRow(variantsSheet, 3, "F5519", "F5519-714", 5, size: "7 1/4");
        WriteVariantRow(variantsSheet, 4, "F5519", "F5519-712", 5, size: "7 1/2");
        WriteVariantRow(variantsSheet, 5, "F5519", "F5519-734", 5, size: "7 3/4");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(4, model.VariantsCreatedCount);

        // No new Size was created — all four already existed in the Hat Size group.
        Assert.Equal(5, await context.Sizes.CountAsync()); // the 4 Hat sizes + the 1 unrelated Footwear "7"

        var variant7 = await context.ProductVariants.AsNoTracking().Include(v => v.Size).FirstAsync(v => v.Sku == "F5519-7");
        // The exact regression: must link to the Hat Size group's OWN "7" (SortOrder 1), never
        // the unrelated Footwear "7" (SortOrder 20) — which is what would put it last, exactly
        // matching the reported wrong order.
        Assert.Equal(hat7.Id, variant7.SizeId);
        Assert.Equal(1, variant7.Size!.SortOrder);

        var variant714 = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "F5519-714");
        var variant712 = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "F5519-712");
        var variant734 = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "F5519-734");
        Assert.Equal(hat714.Id, variant714.SizeId);
        Assert.Equal(hat712.Id, variant712.SizeId);
        Assert.Equal(hat734.Id, variant734.SizeId);
    }

    // Requirement: re-uploading the same file must be enough to fix an already-broken product on
    // production, without SQL — this reproduces the actual broken state (variant already
    // cross-linked to Footwear's "7", as it would be on the live site before this fix) and
    // confirms a plain re-upload corrects it.
    [Fact]
    public async Task BulkUpdate_ReUpload_ExistingVariantWronglyLinkedToOtherGroupsSize_GetsRelinkedToCorrectSize()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var hatSizeGroup = new SizeGroup { Code = "HAT", NameEn = "Hat Size" };
        var footwearGroup = new SizeGroup { Code = "SHOE", NameEn = "Footwear" };
        context.SizeGroups.AddRange(hatSizeGroup, footwearGroup);
        context.SaveChanges();
        var hat7 = new Size { Code = "HAT-7", Label = "7", SizeGroupId = hatSizeGroup.Id, SortOrder = 1 };
        var shoe7 = new Size { Code = "SHOE-7", Label = "7", SizeGroupId = footwearGroup.Id, SortOrder = 20 };
        context.Sizes.AddRange(hat7, shoe7);
        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = hatSizeGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        // The already-broken state: this variant is wrongly linked to Footwear's "7".
        var brokenVariant = new ProductVariant { ProductId = product.Id, SizeId = shoe7.Id, Sku = "F5519-7", StockQuantity = 5 };
        context.ProductVariants.Add(brokenVariant);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 40m, 5);
        // The SAME row a real re-upload of the original file would contain — Size "7" again.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "F5519", "F5519-7", 5, size: "7");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsUpdatedCount);
        Assert.Equal(0, model.VariantsCreatedCount);

        var reloaded = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "F5519-7");
        Assert.Equal(hat7.Id, reloaded.SizeId); // relinked to the correct group's Size
        Assert.NotEqual(shoe7.Id, reloaded.SizeId);
    }

    [Fact]
    public async Task BulkUpdate_ProductWithSizeGroup_NewSizeNotInGroupYet_AutoCreatedWithinThatGroup()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var hatSizeGroup = new SizeGroup { Code = "HAT", NameEn = "Hat Size" };
        context.SizeGroups.Add(hatSizeGroup);
        context.SaveChanges(); // must commit before referencing .Id below — it's still 0 until this runs
        // An UNGROUPED "7 7/8" already exists (e.g. left over from before Size Groups existed) —
        // must not be reused; the new one has to belong to Hat Size, not stay ungrouped.
        var ungrouped778 = new Size { Label = "7 7/8" };
        context.Sizes.Add(ungrouped778);
        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = hatSizeGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 40m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "F5519", "F5519-778", 5, size: "7 7/8");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsCreatedCount);
        Assert.Equal(2, await context.Sizes.CountAsync()); // a NEW size was created — the ungrouped one wasn't reused

        var variant = await context.ProductVariants.AsNoTracking().Include(v => v.Size).FirstAsync(v => v.Sku == "F5519-778");
        Assert.NotEqual(ungrouped778.Id, variant.SizeId);
        Assert.Equal(hatSizeGroup.Id, variant.Size!.SizeGroupId);
    }

    // Confirms "24-7 Agility Pant" (letter clothing sizes, no Size Group at all — the real shape
    // in Data/SeedData.cs) is unaffected by this fix: with no SizeGroupId, size matching stays
    // exactly the global, by-label behavior it always was.
    [Fact]
    public async Task BulkUpdate_ProductWithNoSizeGroup_StillMatchesSizesGlobally()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var existingM = new Size { Label = "M" };
        context.Sizes.Add(existingM);
        var product = new Product
        {
            Sku = "WLM-APP-006", Name = "24-7 Agility Pant", Slug = "24-7-agility-pant", Price = 530.25m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id // SizeGroupId left null, as SeedData.cs leaves it
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "WLM-APP-006", "24-7 Agility Pant", "Tactical Apparel", 530.25m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "WLM-APP-006", "WLM-APP-006-M", 5, size: "M");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, await context.Sizes.CountAsync()); // reused the existing "M", never duplicated
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "WLM-APP-006-M");
        Assert.Equal(existingM.Id, variant.SizeId);
    }

    // Covers the OTHER reasonable reading of "make sure 24-7 Agility Pant isn't affected" — in
    // case it refers to the "Trouser Waist x Length" (TR-WL) Size Group itself (used by
    // Tests.E2E's VariantPreselectGallerySyncTests fixture, not by the real seeded "24-7 Agility
    // Pant", which uses plain letter sizes — see the test above) rather than that product by
    // name: confirms combined Size+Length labels ("28x30") are ALSO matched within their Size
    // Group correctly, not just plain labels.
    [Fact]
    public async Task BulkUpdate_TrouserWaistLengthSizeGroup_CombinedSizeAndLengthMatchedWithinGroup()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var trouserGroup = new SizeGroup { Code = "TR-WL", NameEn = "Trouser Waist x Length" };
        context.SizeGroups.Add(trouserGroup);
        context.SaveChanges(); // must commit before referencing .Id below — it's still 0 until this runs
        var existingCombined = new Size { Label = "28x30", SizeGroupId = trouserGroup.Id, SortOrder = 1 };
        context.Sizes.Add(existingCombined);
        var product = new Product
        {
            Sku = "TRP-001", Name = "Field Trouser", Slug = "field-trouser", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = trouserGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "TRP-001", "Field Trouser", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "TRP-001", "TRP-001-2830", 5, size: "28", length: "30");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, await context.Sizes.CountAsync()); // matched the existing group Size, no duplicate
        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "TRP-001-2830");
        Assert.Equal(existingCombined.Id, variant.SizeId);
    }

    // --- Color auto-create reuses an existing color instead of duplicating ----------------
    // Bug report: colors like "Black"/"Coyote Tan"/"Ranger Green" ended up with 5-6 duplicate
    // rows each in production, created in batches across SEPARATE Bulk Update runs. Root cause:
    // colorsByName (above) was keyed by .Trim() alone, which handles leading/trailing whitespace
    // and case but not an incoming name with extra INTERNAL whitespace ("Coyote  Tan", a stray
    // double space) — that alone was enough to miss the existing row and mint a new one every
    // time. Fixed by keying on ExcelImportHelpers.NormalizeNameForMatching (trim + collapse
    // internal whitespace runs), with "prefer Active, then lowest Id" when more than one existing
    // color already shares a name.

    [Fact]
    public async Task BulkUpdate_SameColorNameAcrossSeparateRuns_DifferentCaseAndExtraInternalSpaces_ReusesOneColor()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "COL-001", Name = "Test Jacket", Slug = "test-jacket", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);

        using (var workbook1 = NewWorkbookWithHeaders())
        {
            WriteProductRow(workbook1.Worksheet("Products"), 2, "COL-001", "Test Jacket", "Tactical Apparel", 100m, 0);
            WriteVariantRow(workbook1.Worksheet("Variants"), 2, "COL-001", "COL-001-A", 5, color: "Coyote Tan");
            var model1 = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook1))).Model);
            Assert.Empty(model1.Errors);
        }

        using (var workbook2 = NewWorkbookWithHeaders())
        {
            WriteProductRow(workbook2.Worksheet("Products"), 2, "COL-001", "Test Jacket", "Tactical Apparel", 100m, 0);
            // Different case AND a doubled internal space — the exact shape that used to slip past .Trim() alone.
            WriteVariantRow(workbook2.Worksheet("Variants"), 2, "COL-001", "COL-001-B", 5, color: "coyote  tan");
            var model2 = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook2))).Model);
            Assert.Empty(model2.Errors);
        }

        var color = await context.Colors.SingleAsync(); // only one row exists at all
        Assert.Equal("Coyote Tan", color.Name); // first-created spelling preserved, not overwritten
        Assert.Equal(2, await context.ProductVariants.CountAsync(v => v.ColorId == color.Id));
    }

    [Fact]
    public async Task BulkUpdate_ColorNameMatchesMultipleExistingColors_PrefersActiveThenLowestId()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        // Two pre-existing colors that collide once case/spacing is ignored — the inactive one
        // was created first (lower Id), so a naive "lowest Id" tie-break alone would pick the
        // wrong one; Active must win first.
        var inactiveOlder = new Color { Name = "Ranger Green", HexCode = "#4b5320", Active = false };
        context.Colors.Add(inactiveOlder);
        context.SaveChanges();
        var activeNewer = new Color { Name = "ranger green", HexCode = "#4b5320", Active = true };
        context.Colors.Add(activeNewer);
        context.SaveChanges();
        Assert.True(inactiveOlder.Id < activeNewer.Id);

        var product = new Product
        {
            Sku = "COL-002", Name = "Test Pant", Slug = "test-pant", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "COL-002", "Test Pant", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "COL-002", "COL-002-A", 5, color: "Ranger Green");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Empty(model.Errors);
        Assert.Equal(2, await context.Colors.CountAsync()); // neither pre-existing duplicate is touched or removed
        var variant = await context.ProductVariants.AsNoTracking().SingleAsync(v => v.Sku == "COL-002-A");
        Assert.Equal(activeNewer.Id, variant.ColorId);
    }

    // --- Size auto-create lands in the correct position, not always at the end ------------
    // Bug report: an auto-created size always got SortOrder = (count of sizes already in the
    // group), i.e. appended last — e.g. in a waist x length group, 26x30/26x32/26x34 ended up
    // AFTER 46x32 instead of before it, and 48x32/46x34/48x34 landed at the end in whatever order
    // the file happened to list them, not sorted by waist then length. Fixed by
    // ComputeSortOrderForNewSize: positions a new size relative to other sizes in the SAME group
    // that fit the SAME recognized pattern (waist x length / plain numeric / standard letter
    // order), shifting existing sizes' SortOrder to make room rather than always appending.

    [Fact]
    public async Task BulkUpdate_AutoCreatedWaistLengthSize_InsertsInNumericOrder_NotAppendedLast()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var trouserGroup = new SizeGroup { Code = "TR-WL", NameEn = "Trouser Waist x Length" };
        context.SizeGroups.Add(trouserGroup);
        context.SaveChanges();
        // Mirrors the production shape: 46x32 already exists; 26x30/26x32/26x34 (smaller waist)
        // need to land BEFORE it once auto-created, not after.
        var existing46x32 = new Size { Label = "46x32", SizeGroupId = trouserGroup.Id, SortOrder = 0 };
        context.Sizes.Add(existing46x32);
        var product = new Product
        {
            Sku = "TRP-010", Name = "Field Trouser 2", Slug = "field-trouser-2", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = trouserGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "TRP-010", "Field Trouser 2", "Tactical Apparel", 100m, 0);
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "TRP-010", "TRP-010-1", 5, size: "26", length: "30");
        WriteVariantRow(workbook.Worksheet("Variants"), 3, "TRP-010", "TRP-010-2", 5, size: "26", length: "32");
        WriteVariantRow(workbook.Worksheet("Variants"), 4, "TRP-010", "TRP-010-3", 5, size: "26", length: "34");
        WriteVariantRow(workbook.Worksheet("Variants"), 5, "TRP-010", "TRP-010-4", 5, size: "48", length: "32");
        WriteVariantRow(workbook.Worksheet("Variants"), 6, "TRP-010", "TRP-010-5", 5, size: "46", length: "34");
        WriteVariantRow(workbook.Worksheet("Variants"), 7, "TRP-010", "TRP-010-6", 5, size: "48", length: "34");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);
        Assert.Empty(model.Errors);

        var sizesByLabel = await context.Sizes
            .Where(s => s.SizeGroupId == trouserGroup.Id)
            .OrderBy(s => s.SortOrder)
            .ToDictionaryAsync(s => s.Label, s => s.SortOrder);

        Assert.Equal(7, sizesByLabel.Count);
        // Correct numeric order by (waist, length): 26x30, 26x32, 26x34, 46x32, 46x34, 48x32, 48x34.
        var orderedLabels = sizesByLabel.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        Assert.Equal(new[] { "26x30", "26x32", "26x34", "46x32", "46x34", "48x32", "48x34" }, orderedLabels);
    }

    [Fact]
    public async Task BulkUpdate_AutoCreatedLetterSize_InsertsInStandardLetterOrder()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var letterGroup = new SizeGroup { Code = "CLO-LTR", NameEn = "Clothing Letter Sizes" };
        context.SizeGroups.Add(letterGroup);
        context.SaveChanges();
        var existingS = new Size { Label = "S", SizeGroupId = letterGroup.Id, SortOrder = 0 };
        var existingM = new Size { Label = "M", SizeGroupId = letterGroup.Id, SortOrder = 1 };
        var existingL = new Size { Label = "L", SizeGroupId = letterGroup.Id, SortOrder = 2 };
        context.Sizes.AddRange(existingS, existingM, existingL);
        var product = new Product
        {
            Sku = "SHT-010", Name = "Field Shirt", Slug = "field-shirt", Price = 60m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id, SizeGroupId = letterGroup.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "SHT-010", "Field Shirt", "Tactical Apparel", 60m, 0);
        // XXS must land FIRST (before S/M/L), and 2XL must land LAST (after S/M/L) — in that one
        // run, so the fix has to get both directions right, not just append-at-end by luck.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "SHT-010", "SHT-010-XXS", 5, size: "XXS");
        WriteVariantRow(workbook.Worksheet("Variants"), 3, "SHT-010", "SHT-010-2XL", 5, size: "2XL");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);
        Assert.Empty(model.Errors);

        var orderedLabels = await context.Sizes
            .Where(s => s.SizeGroupId == letterGroup.Id)
            .OrderBy(s => s.SortOrder)
            .Select(s => s.Label)
            .ToListAsync();

        Assert.Equal(new[] { "XXS", "S", "M", "L", "2XL" }, orderedLabels);
    }

    // --- Descriptions, Vendor SKU, Vendor Color Code -------------------------------------
    // New columns: Products sheet gets Short Description / Short Description (Arabic) /
    // Description / Description (Arabic) / Vendor SKU (after Name (Arabic)); Variants sheet gets
    // Vendor Color Code. See ExcelHeaders/VariantExcelHeaders and ImportVariantsSheet's
    // TryApplyVendorColorCodeAsync.

    private static XLWorkbook NewWorkbookWithDescriptionAndVendorColumns()
    {
        var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders =
        {
            "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL",
            "Name (Arabic)", "Short Description", "Short Description (Arabic)", "Description", "Description (Arabic)", "Vendor SKU",
            "Brand", "Subcategory", "Size Group", "Published"
        };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];

        var variantsSheet = workbook.Worksheets.Add("Variants");
        string[] variantHeaders =
        {
            "Product Sku (Parent)", "Internal Barcode", "Variant Sku", "Color", "Size", "Length",
            "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Stock Quantity", "Image URL", "Vendor Color Code"
        };
        for (var i = 0; i < variantHeaders.Length; i++) variantsSheet.Cell(1, i + 1).Value = variantHeaders[i];

        return workbook;
    }

    [Fact]
    public async Task BulkUpdate_ExportThenReimport_MultiLineAndArabicDescriptionsWithLrm_PreservedExactly()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        const string shortDesc = "Wide-brim boonie hat\nLightweight ripstop fabric";
        const string shortDescAr = "قبعة بوني عريضة الحواف\nقماش خفيف الوزن";
        const string desc = "Line one of the description.\nLine two.\nLine three with more detail.";
        // ‎ is LRM (Left-to-Right Mark) — wraps an embedded English term inside Arabic text,
        // exactly the kind of invisible character the task requires survives untouched.
        const string descAr = "السطر الأول من الوصف.\nالسطر الثاني.\n‎ripstop‎ هو نوع القماش.";

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id,
            ShortDescription = shortDesc, ShortDescriptionAr = shortDescAr,
            Description = desc, DescriptionAr = descAr, VendorSku = "VSKU-100"
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);

        // Real export, then a re-upload of that exact file with nothing changed.
        var exportResult = await controller.ExportExcel();
        var fileResult = Assert.IsType<FileContentResult>(exportResult);
        using var exported = new XLWorkbook(new MemoryStream(fileResult.FileContents));
        var file = ToFormFile(exported);

        var importResult = await controller.BulkUpdate(file);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(importResult).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.UpdatedCount);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "F5519");
        Assert.Equal(shortDesc, reloaded.ShortDescription);
        Assert.Equal(shortDescAr, reloaded.ShortDescriptionAr);
        Assert.Equal(desc, reloaded.Description);
        Assert.Equal(descAr, reloaded.DescriptionAr);
        Assert.Equal("VSKU-100", reloaded.VendorSku);
    }

    [Fact]
    public async Task BulkUpdate_NewDescriptionColumns_BlankCellsLeaveExistingValuesUnchanged_NonBlankCellsApply()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id,
            ShortDescription = "Old short description", Description = "Old description", VendorSku = "OLD-VSKU"
            // ShortDescriptionAr left null on purpose — the row below gives it a value.
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithDescriptionAndVendorColumns();
        var sheet = workbook.Worksheet("Products");
        sheet.Cell(2, ColumnOf(sheet, "Sku")).Value = "F5519";
        sheet.Cell(2, ColumnOf(sheet, "Name")).Value = "Tactical Boonie";
        sheet.Cell(2, ColumnOf(sheet, "Category")).Value = "Tactical Apparel";
        sheet.Cell(2, ColumnOf(sheet, "Price (AED)")).Value = 40m;
        sheet.Cell(2, ColumnOf(sheet, "Stock Quantity")).Value = 5;
        // Short Description and Vendor SKU cells left blank — must stay unchanged.
        sheet.Cell(2, ColumnOf(sheet, "Description")).Value = "New description";
        sheet.Cell(2, ColumnOf(sheet, "Short Description (Arabic)")).Value = "وصف مختصر جديد بالعربية";

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "F5519");
        Assert.Equal("Old short description", reloaded.ShortDescription); // blank cell — unchanged
        Assert.Equal("OLD-VSKU", reloaded.VendorSku); // blank cell — unchanged
        Assert.Equal("New description", reloaded.Description); // non-blank — applied
        Assert.Equal("وصف مختصر جديد بالعربية", reloaded.ShortDescriptionAr); // non-blank — applied
    }

    [Fact]
    public async Task BulkUpdate_OldFormatFileWithoutNewDescriptionColumns_StillImportsCorrectly()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id,
            ShortDescription = "Existing short description", Description = "Existing description"
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders(); // the OLD shape — none of the new columns exist
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 150m, 999);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(150m, reloaded.Price); // the row itself still applies
        Assert.Equal("Existing short description", reloaded.ShortDescription); // untouched — column doesn't exist in this file
        Assert.Equal("Existing description", reloaded.Description); // untouched
    }

    [Fact]
    public async Task BulkUpdate_VariantVendorColorCode_NewVariant_CreatesProductColorWithCode()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithDescriptionAndVendorColumns();
        var variantsSheet = workbook.Worksheet("Variants");
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Product Sku (Parent)")).Value = "F5519";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Variant Sku")).Value = "F5519-BLK";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Color")).Value = "Black";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Stock Quantity")).Value = 5;
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Vendor Color Code")).Value = "001";

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsCreatedCount);

        var productColor = await context.ProductColors.AsNoTracking().Include(pc => pc.Color)
            .FirstOrDefaultAsync(pc => pc.ProductId == product.Id && pc.Color!.Name == "Black");
        Assert.NotNull(productColor);
        Assert.Equal("001", productColor!.VendorColorCode);

        var variant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "F5519-BLK");
        Assert.Equal(productColor.Id, variant.ProductColorId);
    }

    [Fact]
    public async Task BulkUpdate_VariantVendorColorCode_ConflictingCodesForSameProductAndColor_FailsTheConflictingRow()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithDescriptionAndVendorColumns();
        var variantsSheet = workbook.Worksheet("Variants");
        void WriteRow(int row, string variantSku, string vendorColorCode)
        {
            variantsSheet.Cell(row, ColumnOf(variantsSheet, "Product Sku (Parent)")).Value = "F5519";
            variantsSheet.Cell(row, ColumnOf(variantsSheet, "Variant Sku")).Value = variantSku;
            variantsSheet.Cell(row, ColumnOf(variantsSheet, "Color")).Value = "Black";
            variantsSheet.Cell(row, ColumnOf(variantsSheet, "Stock Quantity")).Value = 5;
            variantsSheet.Cell(row, ColumnOf(variantsSheet, "Vendor Color Code")).Value = vendorColorCode;
        }
        // Two sizes of the same Black hat, disagreeing on the vendor color code.
        WriteRow(2, "F5519-BLK-S", "001");
        WriteRow(3, "F5519-BLK-M", "002");

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("conflicts with", model.Errors[0]);
        Assert.Equal(1, model.VariantsCreatedCount); // only the first (non-conflicting) row succeeded
        Assert.False(await context.ProductVariants.AnyAsync(v => v.Sku == "F5519-BLK-M")); // the conflicting row's whole variant was skipped

        var productColor = await context.ProductColors.AsNoTracking().FirstOrDefaultAsync(pc => pc.ProductId == product.Id);
        Assert.NotNull(productColor);
        Assert.Equal("001", productColor!.VendorColorCode); // the first (accepted) row's value wins
    }

    [Fact]
    public async Task BulkUpdate_VariantVendorColorCode_ExistingVariant_UpdatesProductColorsVendorCode()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var black = new Color { Name = "Black", HexCode = "#1c1c1c" };
        context.Colors.Add(black);
        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        var productColor = new ProductColor { Code = "F5519-BLACK", ProductId = product.Id, ColorId = black.Id, VendorColorCode = "OLD-CODE" };
        context.ProductColors.Add(productColor);
        context.SaveChanges();
        context.ProductVariants.Add(new ProductVariant { ProductId = product.Id, ColorId = black.Id, ProductColorId = productColor.Id, Sku = "F5519-BLK", StockQuantity = 5 });
        context.SaveChanges();

        using var workbook = NewWorkbookWithDescriptionAndVendorColumns();
        var variantsSheet = workbook.Worksheet("Variants");
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Product Sku (Parent)")).Value = "F5519";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Variant Sku")).Value = "F5519-BLK";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Stock Quantity")).Value = 5;
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Vendor Color Code")).Value = "NEW-CODE";

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.VariantsUpdatedCount);

        var reloaded = await context.ProductColors.AsNoTracking().FirstAsync(pc => pc.Id == productColor.Id);
        Assert.Equal("NEW-CODE", reloaded.VendorColorCode);
    }

    [Fact]
    public async Task BulkUpdate_VariantVendorColorCode_NoColorOnRow_FailsWithClearError()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithDescriptionAndVendorColumns();
        var variantsSheet = workbook.Worksheet("Variants");
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Product Sku (Parent)")).Value = "F5519";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Variant Sku")).Value = "F5519-NOCOLOR";
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Stock Quantity")).Value = 5;
        variantsSheet.Cell(2, ColumnOf(variantsSheet, "Vendor Color Code")).Value = "001"; // Color cell left blank

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("no Color", model.Errors[0]);
        Assert.Equal(0, model.VariantsCreatedCount);
        Assert.False(await context.ProductVariants.AnyAsync(v => v.Sku == "F5519-NOCOLOR"));
    }

    // --- Products sheet Stock Quantity, for a product that has (or gains) variants -----------
    // Bug report: F5519 has variants, so its own Stock Quantity is supposed to be ignored (the
    // site always uses the sum of its variants' stock instead — see the ChangeTracker backfill
    // in BulkUpdate). But a blank Stock Quantity cell for F5519 failed the WHOLE row with
    // "invalid Stock Quantity value" — the validation never accounted for "this product has
    // variants, so this cell doesn't matter" at all, requiring a value regardless.

    [Fact]
    public async Task BulkUpdate_ProductAlreadyHasVariantsInDatabase_BlankStockQuantityCell_DoesNotFailTheRow()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 999, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, Sku = "F5519-BLK", StockQuantity = 20 },
            new ProductVariant { ProductId = product.Id, Sku = "F5519-KHK", StockQuantity = 13 });
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        // Stock Quantity (column F) deliberately left blank — the exact bug report shape. Only a
        // Products sheet row; no Variants sheet row for F5519 in this file at all (matching the
        // report: this file is just fixing OTHER columns, not touching variants).
        WriteProductRow(workbook.Worksheet("Products"), 2, "F5519", "Tactical Boonie", "Tactical Apparel", 45m, 0);
        workbook.Worksheet("Products").Cell(2, ColumnOf(workbook.Worksheet("Products"), "Stock Quantity")).Clear();

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.UpdatedCount);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "F5519");
        Assert.Equal(45m, reloaded.Price); // the rest of the row still applied
        Assert.Equal(33, reloaded.StockQuantity); // sum of variants (20+13), never the blank cell
    }

    [Fact]
    public async Task BulkUpdate_ProductGainingVariantsInThisSameFile_BlankStockQuantityCell_DoesNotFailTheRow()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        SeedCategoryAndMerchant(context);

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "NEW-VAR-001", "New Boonie", "Tactical Apparel", 45m, 0);
        productsSheet.Cell(2, ColumnOf(productsSheet, "Stock Quantity")).Clear();
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "NEW-VAR-001", "NEW-VAR-001-BLK", 20);
        WriteVariantRow(workbook.Worksheet("Variants"), 3, "NEW-VAR-001", "NEW-VAR-001-KHK", 13);

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.CreatedCount);
        Assert.Equal(2, model.VariantsCreatedCount);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-VAR-001");
        Assert.Equal(33, reloaded.StockQuantity);
    }

    // Regression guard: a product with NO variants (and not gaining any in this file) must keep
    // requiring a valid Stock Quantity — this fix only relaxes the rule for products variants
    // actually make the cell moot for.
    [Fact]
    public async Task BulkUpdate_ProductWithNoVariants_BlankStockQuantityCell_StillFailsTheRow()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "152", "Field Pants", "Tactical Apparel", 150m, 0);
        productsSheet.Cell(2, ColumnOf(productsSheet, "Stock Quantity")).Clear();

        var controller = CreateController(context);
        var model = Assert.IsType<BulkImportResult>(Assert.IsType<ViewResult>(await controller.BulkUpdate(ToFormFile(workbook))).Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("invalid Stock Quantity value", model.Errors[0]);
        Assert.Equal(0, model.UpdatedCount);

        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(100m, reloaded.Price); // whole row skipped, nothing applied
        Assert.Equal(50, reloaded.StockQuantity);
    }

    // --- Export: Stock Quantity reflects the LIVE variant sum, not a possibly-stale field -----
    // Bug report continuation: the exported Stock Quantity cell for F5519 didn't show its real
    // (summed) stock. Product.StockQuantity is only ever re-synced to that sum as a side effect
    // of a successful Bulk Update run — it's a plain stored field, not a computed one — so it can
    // legitimately go stale (e.g. every recent import attempt for F5519 failing on the bug above
    // meant its stored field never got the chance to catch up). Export now computes the sum
    // directly instead of trusting the stored field.

    [Fact]
    public async Task ExportExcel_ProductWithVariants_WritesTheLiveSummedStock_NotTheStaleStoredField()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 999, // deliberately stale/wrong — variants below actually sum to 33
            CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, Sku = "F5519-BLK", StockQuantity = 20 },
            new ProductVariant { ProductId = product.Id, Sku = "F5519-KHK", StockQuantity = 13 });
        context.SaveChanges();

        var controller = CreateController(context);
        var fileResult = Assert.IsType<FileContentResult>(await controller.ExportExcel());
        using var exported = new XLWorkbook(new MemoryStream(fileResult.FileContents));
        var sheet = exported.Worksheet("Products");
        var stockCol = ColumnOf(sheet, "Stock Quantity");
        var row = sheet.RowsUsed().Skip(1).First(r => r.Cell(ColumnOf(sheet, "Sku")).GetString().Trim() == "F5519");

        Assert.Equal(33, row.Cell(stockCol).GetValue<int>());
    }

    // Regression guard: a product with no variants at all still exports its own plain
    // StockQuantity field — there's no variant sum to prefer it over.
    [Fact]
    public async Task ExportExcel_ProductWithNoVariants_WritesItsOwnStockQuantityField()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 371, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);
        var fileResult = Assert.IsType<FileContentResult>(await controller.ExportExcel());
        using var exported = new XLWorkbook(new MemoryStream(fileResult.FileContents));
        var sheet = exported.Worksheet("Products");
        var stockCol = ColumnOf(sheet, "Stock Quantity");
        var row = sheet.RowsUsed().Skip(1).First(r => r.Cell(ColumnOf(sheet, "Sku")).GetString().Trim() == "152");

        Assert.Equal(371, row.Cell(stockCol).GetValue<int>());
    }
}
