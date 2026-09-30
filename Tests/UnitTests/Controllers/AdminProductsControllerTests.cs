using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// Areas/Admin/Controllers/ProductsController.cs's Edit action: the admin Product form (Name/
/// Short Description/Description now each have an Arabic sibling, see Views/Products/_Form.cshtml)
/// and the "Vendor Color Codes" table's self-healing ProductColor backfill (see
/// ProductsController.BackfillProductColorsFromVariants and the sibling coverage in
/// ProductsControllerBulkUpdateTests for Bulk Update's own copy of this same backfill).
///
/// Bug report: product F5519 "Tactical Boonie" (created via Bulk Update, 8 variants across
/// Black/Khaki) showed nothing at all below the Save button on its Edit page — Edit.cshtml's
/// "Vendor Color Codes" table only renders when Product.ProductColors.Count > 0, and nothing in
/// Bulk Update (before this fix) ever created a ProductColor row, only ProductVariant.ColorId.
/// </summary>
public class AdminProductsControllerTests
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

        var httpContext = new DefaultHttpContext();
        return new ProductsController(context, localizer.Object, outputCache.Object, NullLogger<ProductsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            // Edit(int, Product) sets TempData["AdminMessage"] on success — Controller.TempData
            // otherwise resolves ITempDataDictionaryFactory from HttpContext.RequestServices,
            // which a bare DefaultHttpContext doesn't have, throwing a NullReferenceException.
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }

    private static (Category Category, Merchant Merchant) SeedCategoryAndMerchant(ApplicationDbContext context)
    {
        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        context.Categories.Add(category);
        context.Users.Add(user);
        context.Merchants.Add(merchant);
        context.SaveChanges();
        return (category, merchant);
    }

    [Fact]
    public async Task Edit_Get_ProductWithColorVariantsButNoProductColors_BackfillsThemAndReturnsInViewBag()
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
        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, ColorId = black.Id, Sku = "F5519-BLK", StockQuantity = 5 },
            new ProductVariant { ProductId = product.Id, ColorId = khaki.Id, Sku = "F5519-KHK", StockQuantity = 5 });
        context.SaveChanges();

        var controller = CreateController(context);
        var actionResult = await controller.Edit(product.Id);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var productColors = Assert.IsAssignableFrom<IEnumerable<ProductColor>>(viewResult.ViewData["ProductColors"]).ToList();

        // The exact bug: before this fix, this list was always empty for a Bulk-Update-only
        // product, so Edit.cshtml's "Vendor Color Codes" table never rendered at all.
        Assert.Equal(2, productColors.Count);
        Assert.Contains(productColors, pc => pc.Color!.Name == "Black");
        Assert.Contains(productColors, pc => pc.Color!.Name == "Khaki");

        // Persisted, not just returned for this one request — reloading fresh confirms it.
        var reloadedCount = await context.ProductColors.AsNoTracking().CountAsync(pc => pc.ProductId == product.Id);
        Assert.Equal(2, reloadedCount);

        var reloadedVariants = await context.ProductVariants.AsNoTracking().Where(v => v.ProductId == product.Id).ToListAsync();
        Assert.All(reloadedVariants, v => Assert.NotNull(v.ProductColorId));
    }

    [Fact]
    public async Task Edit_Get_ProductWithNoColorVariants_LeavesProductColorsEmpty_NoWritesMade()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "PLAIN-001", Name = "No Colors Here", Slug = "no-colors-here", Price = 20m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);
        var actionResult = await controller.Edit(product.Id);

        var viewResult = Assert.IsType<ViewResult>(actionResult);
        var productColors = Assert.IsAssignableFrom<IEnumerable<ProductColor>>(viewResult.ViewData["ProductColors"]).ToList();
        Assert.Empty(productColors);
        Assert.Equal(0, await context.ProductColors.CountAsync());
    }

    [Fact]
    public async Task Edit_Get_ProductAlreadyHasProductColors_DoesNotDuplicate()
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
        var existingProductColor = new ProductColor { Code = "F5519-BLACK", ProductId = product.Id, ColorId = black.Id, DefaultColor = true };
        context.ProductColors.Add(existingProductColor);
        context.SaveChanges();
        context.ProductVariants.Add(new ProductVariant { ProductId = product.Id, ColorId = black.Id, ProductColorId = existingProductColor.Id, Sku = "F5519-BLK", StockQuantity = 5 });
        context.SaveChanges();

        var controller = CreateController(context);
        await controller.Edit(product.Id);

        Assert.Equal(1, await context.ProductColors.CountAsync(pc => pc.ProductId == product.Id));
    }

    [Fact]
    public async Task Edit_Post_PersistsArabicNameShortDescriptionAndDescription()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        var product = new Product
        {
            Sku = "F5519", Name = "Tactical Boonie", Slug = "tactical-boonie", Price = 40m,
            StockQuantity = 5, CategoryId = category.Id, MerchantId = merchant.Id,
            ShortDescription = "Wide-brim boonie hat", Description = "A field-tested boonie hat."
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);
        var posted = new Product
        {
            Id = product.Id,
            Name = product.Name,
            NameAr = "قبعة بوني تكتيكية",
            Slug = product.Slug,
            CategoryId = category.Id,
            ShortDescription = product.ShortDescription,
            ShortDescriptionAr = "قبعة بوني عريضة الحواف",
            Description = product.Description,
            DescriptionAr = "قبعة بوني مختبرة ميدانياً.",
            Price = product.Price,
            Sku = product.Sku,
            StockQuantity = product.StockQuantity
        };

        var actionResult = await controller.Edit(product.Id, posted);

        Assert.IsType<RedirectToActionResult>(actionResult);
        var reloaded = await context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        Assert.Equal("قبعة بوني تكتيكية", reloaded.NameAr);
        Assert.Equal("قبعة بوني عريضة الحواف", reloaded.ShortDescriptionAr);
        Assert.Equal("قبعة بوني مختبرة ميدانياً.", reloaded.DescriptionAr);
        // English fields untouched by this round trip.
        Assert.Equal("Wide-brim boonie hat", reloaded.ShortDescription);
        Assert.Equal("A field-tested boonie hat.", reloaded.Description);
    }

    // --- ProductMedia self-heal (opening Edit fixes the gallery without a re-upload) --------
    // Bug report continuation: ProductPhotosController.Upload's per-color-batch bug (see
    // ProductMediaOrderingHelperTests) left F5519's Black/Khaki media both marked IsColorMain
    // with colliding DisplayOrder. Requirement: opening the Edit page must be enough to repair
    // an already-broken product, no re-upload or SQL required.

    [Fact]
    public async Task Edit_Get_ProductWithWronglyOrderedMedia_NormalizesIsColorMainAndDisplayOrder()
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
        var productColor = new ProductColor { Code = "F5519-BLACK", ProductId = product.Id, ColorId = black.Id, DefaultColor = true };
        context.ProductColors.Add(productColor);
        context.SaveChanges();
        var variant = new ProductVariant { ProductId = product.Id, ColorId = black.Id, ProductColorId = productColor.Id, Sku = "F5519-BLK", StockQuantity = 5 };
        context.ProductVariants.Add(variant);
        // The exact broken state ProductPhotosController.Upload's bug produced: both real
        // photos for Black marked IsColorMain=true, both DisplayOrder=1.
        const string mainUrl = "https://blob.example/product-photos/f5519-001.webp";
        const string bottomUrl = "https://blob.example/product-photos/f5519-001-2.webp";
        context.ProductMedia.AddRange(
            new ProductMedia { ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = mainUrl, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true },
            new ProductMedia { ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = bottomUrl, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true });
        context.SaveChanges();

        var controller = CreateController(context);

        // Just opening the Edit page — no upload, no SQL.
        await controller.Edit(product.Id);

        var media = await context.ProductMedia.AsNoTracking().Where(m => m.ProductColorId == productColor.Id).ToListAsync();
        var main = media.First(m => m.MediaUrl == mainUrl);
        var bottom = media.First(m => m.MediaUrl == bottomUrl);
        Assert.True(main.IsColorMain);
        Assert.Equal(1, main.DisplayOrder);
        Assert.False(bottom.IsColorMain);
        Assert.Equal(2, bottom.DisplayOrder);

        // Listing card / cart image also gets refreshed to the now-correct main photo.
        var reloadedVariant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Id == variant.Id);
        Assert.Equal(mainUrl, reloadedVariant.ImageUrl);
    }

    [Fact]
    public async Task Edit_Get_ProductWithNoMedia_LeavesProductMediaEmpty_NoWritesMade()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (category, merchant) = SeedCategoryAndMerchant(context);

        // Mirrors "24-7 Agility Pant": no ProductMedia at all (never went through Product Photos
        // or the Product Importer's Media sheet) — must be completely unaffected.
        var product = new Product
        {
            Sku = "WLM-APP-006", Name = "24-7 Agility Pant", Slug = "24-7-agility-pant", Price = 530.25m,
            StockQuantity = 90, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        var controller = CreateController(context);
        await controller.Edit(product.Id);

        Assert.Equal(0, await context.ProductMedia.CountAsync());
    }
}
