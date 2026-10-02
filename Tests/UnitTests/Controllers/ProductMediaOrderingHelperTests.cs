using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// Bug report: uploading multiple photos for the same color in one Admin -> Product Photos batch
/// (e.g. F5519 "Tactical Boonie"'s Black main + Black bottom-view) left every one of them marked
/// IsColorMain=true with a colliding DisplayOrder=1 — root cause was ProductPhotosController.Upload
/// re-querying ProductMedia from the DATABASE once per file, inside a loop that only calls
/// SaveChangesAsync once at the very end, so a second file for the same color never saw the first
/// file's not-yet-saved row and treated itself as "the first upload" too (see
/// ProductPhotosController.Upload's GetMediaForColorAsync remarks for that half of the fix).
///
/// This file covers the other half: ProductMediaOrderingHelper, which re-derives IsColorMain and
/// DisplayOrder purely from each row's own MediaUrl (whether it has a "-suffix" or not) rather than
/// upload order or a stored flag — the property that makes it "self-healing": calling it again on
/// already-wrong data (from before this fix existed) converges on the correct state regardless of
/// how it got wrong, which is what lets a plain re-upload or just opening the Edit page repair a
/// product on production without SQL (see AdminProductsControllerTests for that Edit-page path).
/// </summary>
public class ProductMediaOrderingHelperTests
{
    private const string Black001 = "https://blob.example/product-photos/f5519-001.webp"; // main
    private const string Black001Bottom = "https://blob.example/product-photos/f5519-001-2.webp"; // extra angle

    [Theory]
    [InlineData("https://blob.example/product-photos/f5519-001.webp", true)]
    [InlineData("https://blob.example/product-photos/f5519-001-2.webp", false)]
    [InlineData("https://blob.example/product-photos/f5519-001-back.webp", false)] // non-numeric suffix no longer matches the pattern at all, but that's still "not main"
    [InlineData("https://blob.example/product-photos/101228-002.webp", true)]
    public void IsMainMediaUrl_DerivesFromFilenameSuffixAlone(string url, bool expectedMain)
    {
        Assert.Equal(expectedMain, ProductMediaOrderingHelper.IsMainMediaUrl(url));
    }

    [Fact]
    public void NormalizeColorMediaOrdering_BothRowsWronglyMarkedMainWithCollidingOrder_CorrectsBoth()
    {
        // Reproduces the exact broken state the bug produced: two real photos for the same color,
        // both IsColorMain=true, both DisplayOrder=1 (the ProductPhotosController.Upload bug).
        var main = new ProductMedia { MediaUrl = Black001, IsColorMain = true, DisplayOrder = 1 };
        var bottom = new ProductMedia { MediaUrl = Black001Bottom, IsColorMain = true, DisplayOrder = 1 };

        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(new[] { main, bottom });

        Assert.True(main.IsColorMain);
        Assert.Equal(1, main.DisplayOrder);
        Assert.False(bottom.IsColorMain);
        Assert.Equal(2, bottom.DisplayOrder);
    }

    [Fact]
    public void NormalizeColorMediaOrdering_LeavesPlaceholderRowsUntouched()
    {
        var main = new ProductMedia { MediaUrl = Black001, IsColorMain = false, DisplayOrder = 0 };
        var placeholder = new ProductMedia { MediaUrl = ProductMediaOrderingHelper.PlaceholderMediaUrl, IsColorMain = true, DisplayOrder = 99 };

        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(new[] { main, placeholder });

        Assert.True(main.IsColorMain);
        Assert.Equal(1, main.DisplayOrder);
        // Untouched — a placeholder is never a real photo, so it has no "main-ness" of its own.
        Assert.True(placeholder.IsColorMain);
        Assert.Equal(99, placeholder.DisplayOrder);
    }

    [Fact]
    public void NormalizeColorMediaOrdering_IsIdempotent()
    {
        var main = new ProductMedia { MediaUrl = Black001, IsColorMain = true, DisplayOrder = 1 };
        var bottom = new ProductMedia { MediaUrl = Black001Bottom, IsColorMain = false, DisplayOrder = 2 };
        var media = new[] { main, bottom };

        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(media);
        var firstPassMain = (main.IsColorMain, main.DisplayOrder);
        var firstPassBottom = (bottom.IsColorMain, bottom.DisplayOrder);

        // Calling it again — e.g. a second Edit page visit, or a second re-upload — must not
        // change anything once it's already correct.
        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(media);

        Assert.Equal(firstPassMain, (main.IsColorMain, main.DisplayOrder));
        Assert.Equal(firstPassBottom, (bottom.IsColorMain, bottom.DisplayOrder));
    }

    [Fact]
    public void NormalizeColorMediaOrdering_ThreeAngles_OrdersMainFirstThenBySuffix()
    {
        var back = new ProductMedia { MediaUrl = "https://blob.example/product-photos/f5519-001-3.webp" };
        var main = new ProductMedia { MediaUrl = Black001 };
        var bottom = new ProductMedia { MediaUrl = Black001Bottom };

        // Deliberately shuffled input order — the result must not depend on it.
        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(new[] { back, main, bottom });

        Assert.Equal(1, main.DisplayOrder);
        Assert.True(main.IsColorMain);
        Assert.Equal(2, bottom.DisplayOrder);
        Assert.False(bottom.IsColorMain);
        Assert.Equal(3, back.DisplayOrder);
        Assert.False(back.IsColorMain);
    }

    // Supports extra gallery photos per product color: a "-N" suffixed photo (N = 2..20) must
    // order NUMERICALLY, not alphabetically by URL — plain string ordering would put "-10"
    // between "-1" and "-2", ahead of "-9". This is the same DisplayOrder the PDP gallery
    // (Views/Products/Details.cshtml's colorThumbItems) renders a color's photos in, so this is
    // also the regression guard for "gallery shows main first, then extras in order" once a
    // color has 10 or more photos.
    [Fact]
    public void NormalizeColorMediaOrdering_TenOrMorePhotos_OrdersNumericallyNotAlphabetically()
    {
        var photo10 = new ProductMedia { MediaUrl = "https://blob.example/product-photos/f5519-001-10.webp" };
        var photo2 = new ProductMedia { MediaUrl = "https://blob.example/product-photos/f5519-001-2.webp" };
        var photo9 = new ProductMedia { MediaUrl = "https://blob.example/product-photos/f5519-001-9.webp" };
        var main = new ProductMedia { MediaUrl = Black001 };

        // Shuffled input, and deliberately in an order a plain alphabetical sort of the URL would
        // have reproduced anyway ("...-10" < "...-2" < "...-9" as strings) — if the fix regressed
        // back to string ordering, this would still "accidentally" look right unless the
        // assertions below check the actual DisplayOrder values, not just relative positions.
        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(new[] { photo10, main, photo9, photo2 });

        Assert.Equal(1, main.DisplayOrder);
        Assert.True(main.IsColorMain);
        Assert.Equal(2, photo2.DisplayOrder);
        Assert.Equal(3, photo9.DisplayOrder);
        Assert.Equal(4, photo10.DisplayOrder); // numerically last, not alphabetically first among the extras
        Assert.False(photo2.IsColorMain);
        Assert.False(photo9.IsColorMain);
        Assert.False(photo10.IsColorMain);
    }

    [Theory]
    [InlineData("https://blob.example/product-photos/101119-029.jpg", null)] // main photo — no number
    [InlineData("https://blob.example/product-photos/101119-029-2.jpg", 2)]
    [InlineData("https://blob.example/product-photos/101119-029-20.jpg", 20)]
    [InlineData("https://blob.example/product-photos/101119-029-zip-pocket.jpg", null)] // never silently parsed as a number
    public void GetExtraPhotoNumber_MatchesTryParsePhotoFileNamesOwnRules(string url, int? expected)
    {
        Assert.Equal(expected, ProductMediaOrderingHelper.GetExtraPhotoNumber(url));
    }

    private static (ApplicationDbContext Context, SqliteConnection Connection) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    [Fact]
    public async Task SyncStorefrontDisplayFieldsAsync_UsesTheNormalizedMainPhoto_ForVariantAndProductImageUrl()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;

        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        var black = new Color { Name = "Black", HexCode = "#1c1c1c" };
        context.Categories.Add(category);
        context.Users.Add(user);
        context.Merchants.Add(merchant);
        context.Colors.Add(black);
        context.SaveChanges();

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
        // Reproduces the broken state directly: both marked main, colliding order.
        var mainMedia = new ProductMedia { ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = Black001, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true };
        var bottomMedia = new ProductMedia { ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = Black001Bottom, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true };
        context.ProductMedia.AddRange(mainMedia, bottomMedia);
        context.SaveChanges();

        ProductMediaOrderingHelper.NormalizeColorMediaOrdering(new[] { mainMedia, bottomMedia });
        await context.SaveChangesAsync();
        await ProductMediaOrderingHelper.SyncStorefrontDisplayFieldsAsync(context, new[] { product.Id });

        var reloadedVariant = await context.ProductVariants.AsNoTracking().FirstAsync(v => v.Id == variant.Id);
        var reloadedProduct = await context.Products.AsNoTracking().FirstAsync(p => p.Id == product.Id);
        // Must be the MAIN (no-suffix) photo — not whichever tied row an unordered SQL result
        // happened to return first, which is what the bug non-deterministically produced.
        Assert.Equal(Black001, reloadedVariant.ImageUrl);
        Assert.Equal(Black001, reloadedProduct.ImageUrl);
    }
}
