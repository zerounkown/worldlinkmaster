using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.E2E.IntegrationTests;

/// <summary>
/// Coverage for the Bulk Update (Excel) columns added for onboarding supplier catalogs: Name
/// (Arabic), Brand, Subcategory, Size Group, Published on the Products sheet, and Barcode on the
/// Variants sheet — plus the rules that go with them (blank cell never wipes a value; Brand/
/// Subcategory/Size Group match existing records by name only and are never auto-created;
/// Barcode must be unique; a product with variants always has its Stock Quantity recomputed as
/// the sum of its variants' stock; a new product defaults to unpublished).
///
/// Runs against a real Postgres connection (E2E_POSTGRES_CONNECTION — the disposable container in
/// CI, never Supabase), for the same reason as the sibling BulkUpdateExecutionStrategyTests: the
/// import runs inside _context.Database.CreateExecutionStrategy().ExecuteAsync(...), which SQLite
/// can exercise but can't meaningfully validate (no retrying strategy to get wrong), and Barcode
/// uniqueness is also enforced by a real Postgres unique index (see ApplicationDbContext) that
/// these tests rely on as a backstop.
///
/// Joins E2ETestCollection (see PostgresTestSchema's remarks) purely for sequencing: it doesn't
/// use the Browser/BaseUrl those fixtures provide, but being in that collection makes xUnit run
/// this test only after E2EWebAppFactory's own Migrate() has already fully committed, and never
/// concurrently with a sibling Postgres-backed test class's EnsureCreatedAsync() — both of which,
/// left concurrent, raced over the database-wide "CREATE EXTENSION IF NOT EXISTS pg_trgm" step.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class BulkUpdateColumnsTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("E2E_POSTGRES_CONNECTION")
        ?? throw new InvalidOperationException(
            "The E2E_POSTGRES_CONNECTION environment variable is not set. Point it at a Postgres " +
            "instance this suite is allowed to create/drop a \"bulk_update_columns_test\" schema " +
            "in (e.g. a local/CI Postgres container) — same requirement as every other test in " +
            "Tests.E2E (see E2EWebAppFactory). Never point this at the shared Supabase instance " +
            "for a routine local run.");

    private const string SchemaName = "bulk_update_columns_test";

    private ApplicationDbContext _context = null!;

    public async Task InitializeAsync()
    {
        await PostgresTestSchema.ResetAsync(ConnectionString, SchemaName);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql($"{ConnectionString};Search Path={SchemaName}", npgsql =>
            {
                npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                npgsql.CommandTimeout(60);
            })
            .Options;

        _context = new ApplicationDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await PostgresTestSchema.DropAsync(ConnectionString, SchemaName);
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

    private async Task<(Category Category, Merchant Merchant)> SeedCategoryAndMerchantAsync()
    {
        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "World Link Master", Slug = "world-link-master" };
        _context.Categories.Add(category);
        _context.Users.Add(user);
        _context.Merchants.Add(merchant);
        await _context.SaveChangesAsync();
        return (category, merchant);
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

    // Products row helper — leaves a cell truly blank when the corresponding argument is null,
    // matching how ClosedXML represents an untouched cell (IsEmpty() == true), not an empty string.
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

    [Fact]
    public async Task BulkUpdate_BlankCellsLeaveExistingValuesUnchanged_NonBlankCellsApply()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var brand = new Brand { Name = "Condor", Slug = "condor" };
        _context.Brands.Add(brand);
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, WholesalePrice = 25m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id, IsPublished = true
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        // Wholesale Price, Subcategory, Size Group, and Published are all left blank — none of
        // them should change. Name (Arabic) and Brand ARE provided and should apply.
        WriteProductRow(productsSheet, 2, "152", "Field Pants", "Tactical Apparel", 110m, 999 /* ignored: no variants on this product */,
            nameAr: "بنطال ميداني", brand: "Condor");

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(1, model.UpdatedCount);

        var reloaded = await FreshContext().Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(110m, reloaded.Price);
        Assert.Equal(25m, reloaded.WholesalePrice); // untouched — blank cell
        Assert.Equal("بنطال ميداني", reloaded.NameAr); // applied — non-blank cell
        Assert.Equal(brand.Id, reloaded.BrandId); // applied — non-blank cell
        Assert.Null(reloaded.SubcategoryId); // untouched — blank cell
        Assert.Null(reloaded.SizeGroupId); // untouched — blank cell
        Assert.True(reloaded.IsPublished); // untouched — blank cell
        Assert.Equal(999, reloaded.StockQuantity); // no variants on this product — Products sheet value applies
    }

    [Fact]
    public async Task BulkUpdate_UnrecognizedBrand_FailsThatRowWithoutTouchingTheProduct()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 50, CategoryId = category.Id, MerchantId = merchant.Id
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "152", "Field Pants", "Tactical Apparel", 200m, 999, brand: "NoSuchBrand");

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Equal(0, model.UpdatedCount);
        Assert.Single(model.Errors);
        Assert.Contains("unrecognized Brand", model.Errors[0]);
        Assert.Contains("NoSuchBrand", model.Errors[0]);

        var reloaded = await FreshContext().Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(100m, reloaded.Price); // the whole row was skipped, not just the Brand part
        Assert.Null(reloaded.BrandId);
        Assert.Equal(0, await _context.Brands.CountAsync()); // never auto-created
    }

    [Fact]
    public async Task BulkUpdate_DuplicateBarcode_InFileAndAgainstDatabase_FailsWithoutCrashing()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        var variantA = new ProductVariant { ProductId = product.Id, Sku = "152-A", StockQuantity = 1, Barcode = "1111111111" };
        var variantB = new ProductVariant { ProductId = product.Id, Sku = "152-B", StockQuantity = 2, Barcode = null };
        _context.ProductVariants.AddRange(variantA, variantB);
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);

        var variantsSheet = workbook.Worksheet("Variants");
        // Row 2: try to give variant B the barcode variant A already has — must fail.
        WriteVariantRow(variantsSheet, 2, "152", "152-B", 2, barcode: "1111111111");
        // Rows 3-4: two BRAND NEW variants both claiming the same never-before-seen barcode — the
        // second must fail against the first, entirely within this one file.
        WriteVariantRow(variantsSheet, 3, "152", "152-C", 3, barcode: "2222222222");
        WriteVariantRow(variantsSheet, 4, "152", "152-D", 4, barcode: "2222222222");

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Equal(2, model.Errors.Count);
        Assert.Contains(model.Errors, e => e.Contains("152-B") && e.Contains("already belongs to variant '152-A'"));
        Assert.Contains(model.Errors, e => e.Contains("152-D") && e.Contains("already belongs to variant '152-C'"));

        // The valid row (152-C) still went through despite the other two failing.
        Assert.Equal(1, model.VariantsCreatedCount);
        var freshContext = FreshContext();
        Assert.Null((await freshContext.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-B")).Barcode);
        Assert.Equal("2222222222", (await freshContext.ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-C")).Barcode);
        Assert.False(await freshContext.ProductVariants.AsNoTracking().AnyAsync(v => v.Sku == "152-D"));
    }

    [Fact]
    public async Task BulkUpdate_ProductWithVariants_StockQuantityIsAlwaysTheSumOfVariantStock()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var product = new Product
        {
            Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m,
            StockQuantity = 999, // deliberately stale/wrong, to prove it gets overridden
            CategoryId = category.Id, MerchantId = merchant.Id
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        // Neither variant is mentioned anywhere in the uploaded file below — the sum still has to
        // be computed from them, not just from whatever this file happens to touch.
        _context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, Sku = "152-A", StockQuantity = 3 },
            new ProductVariant { ProductId = product.Id, Sku = "152-B", StockQuantity = 4 });
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        // Stock Quantity cell says 50 — must be ignored in favor of the variant sum (7).
        WriteProductRow(productsSheet, 2, "152", "Field Pants", "Tactical Apparel", 100m, 50);

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);

        var reloaded = await FreshContext().Products.AsNoTracking().FirstAsync(p => p.Sku == "152");
        Assert.Equal(7, reloaded.StockQuantity);
    }

    [Fact]
    public async Task BulkUpdate_NewProduct_DefaultsToUnpublished_UnlessPublishedColumnSaysYes()
    {
        var (category, _) = await SeedCategoryAndMerchantAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        WriteProductRow(productsSheet, 2, "NEW-001", "Unpublished By Default", "Tactical Apparel", 50m, 10); // Published left blank
        WriteProductRow(productsSheet, 3, "NEW-002", "Published Explicitly", "Tactical Apparel", 60m, 20, published: "Yes");

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        Assert.Equal(2, model.CreatedCount);

        var freshContext = FreshContext();
        Assert.False((await freshContext.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-001")).IsPublished);
        Assert.True((await freshContext.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-002")).IsPublished);
    }

    [Fact]
    public async Task BulkUpdate_SubcategoryMustBelongToCategory_WrongCategoryFailsTheRow()
    {
        var (category, _) = await SeedCategoryAndMerchantAsync();
        var otherCategory = new Category { Code = "FTW", Name = "Footwear", Slug = "footwear" };
        _context.Categories.Add(otherCategory);
        var rightSubcategory = new Subcategory { Name = "Trousers", Slug = "trousers", CategoryId = category.Id };
        var wrongSubcategory = new Subcategory { Name = "Boots", Slug = "boots", CategoryId = otherCategory.Id };
        _context.Subcategories.AddRange(rightSubcategory, wrongSubcategory);
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        var productsSheet = workbook.Worksheet("Products");
        // "Boots" exists, but under Footwear, not Tactical Apparel — must fail for this new product.
        WriteProductRow(productsSheet, 2, "NEW-003", "Should Fail", "Tactical Apparel", 50m, 10, subcategory: "Boots");
        // "Trousers" exists under the right category — must succeed.
        WriteProductRow(productsSheet, 3, "NEW-004", "Should Succeed", "Tactical Apparel", 50m, 10, subcategory: "Trousers");

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("NEW-003", model.Errors[0]);
        Assert.Contains("unrecognized Subcategory", model.Errors[0]);
        Assert.Equal(1, model.CreatedCount);

        var freshContext = FreshContext();
        Assert.False(await freshContext.Products.AnyAsync(p => p.Sku == "NEW-003"));
        var succeeded = await freshContext.Products.AsNoTracking().FirstAsync(p => p.Sku == "NEW-004");
        Assert.Equal(rightSubcategory.Id, succeeded.SubcategoryId);
    }

    // The site stores prices INCLUDING 5% VAT (ProductVariant.Price). "Price (AED)" is the
    // excl.-VAT price, "Price+VAT" the already-inclusive one — see TryResolveVariantPrice.
    [Fact]
    public async Task BulkUpdate_VariantPriceColumnsDisagree_FailsTheRowAndSavesNothing()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        using var workbook = NewWorkbookWithHeaders();
        WriteProductRow(workbook.Worksheet("Products"), 2, "152", "Field Pants", "Tactical Apparel", 100m, 0);
        // 100 x 1.05 = 105.00, but Price+VAT says 999 — well outside the 0.01 tolerance.
        WriteVariantRow(workbook.Worksheet("Variants"), 2, "152", "152-VAT-MISMATCH", 5, priceExclVat: 100m, priceInclVat: 999m);

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Single(model.Errors);
        Assert.Contains("doesn't match", model.Errors[0]);
        Assert.Equal(0, model.VariantsCreatedCount);
        Assert.False(await FreshContext().ProductVariants.AnyAsync(v => v.Sku == "152-VAT-MISMATCH"));
    }

    // Old exports (no "Price+VAT" column at all) meant "Price (AED)" as the VAT-INCLUSIVE price
    // directly — that meaning has to be preserved exactly, never reinterpreted as the new
    // excl.-VAT column of the same name.
    [Fact]
    public async Task BulkUpdate_OldFormatFile_NoPriceVatColumnAtAll_TreatsPriceAedAsAlreadyVatInclusive()
    {
        var (category, merchant) = await SeedCategoryAndMerchantAsync();
        var product = new Product { Sku = "152", Name = "Field Pants", Slug = "field-pants", Price = 100m, StockQuantity = 0, CategoryId = category.Id, MerchantId = merchant.Id };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

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

        var file = ToFormFile(workbook);
        var controller = CreateController(_context);

        var actionResult = await controller.BulkUpdate(file);
        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var model = Assert.IsType<BulkImportResult>(viewResult.Model);

        Assert.Null(model.FatalError);
        Assert.Empty(model.Errors);
        var variant = await FreshContext().ProductVariants.AsNoTracking().FirstAsync(v => v.Sku == "152-OLD");
        // Must NOT be 105 x 1.05 = 110.25 — that would be double-applying VAT.
        Assert.Equal(105.00m, variant.Price);
    }

    // A brand-new DbContext against the same schema, so assertions read back what was actually
    // committed to the database rather than the original context's (already-correct, but
    // unproven) in-memory tracked state.
    private ApplicationDbContext FreshContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql($"{ConnectionString};Search Path={SchemaName}")
            .Options);
}
