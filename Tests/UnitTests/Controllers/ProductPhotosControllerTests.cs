using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// ProductPhotosController.TryParsePhotoFileName — the "{VendorSku}-{VendorColorCode}[-N].ext"
/// filename parser Upload() uses to decide whether an uploaded file is a color's main photo, one
/// of its extra gallery photos (N = 2..20), or not a match at all. Extracted out of Upload()
/// itself (which requires a configured Azure Blob connection before it reaches this logic at
/// all) specifically so it's unit-testable without standing up blob storage.
///
/// Bug this guards against: the "-N" suffix used to accept ANY alphanumeric text (e.g.
/// "101119-029-zip-pocket.jpg"), which Upload() would silently add to the gallery as if it were
/// a real extra photo. The suffix is now numeric-only, 2-20, so a name like that is rejected —
/// never silently matched to the main photo, and never silently accepted as an extra one either.
/// </summary>
public class ProductPhotosControllerTests
{
    [Theory]
    [InlineData("101119-029.jpg")]
    [InlineData("101119-029.jpeg")]
    [InlineData("101119-029.png")]
    [InlineData("101119-029.webp")]
    [InlineData("101119-029.JPG")] // extension casing is ignored
    [InlineData("F5259-BLK.webp")] // alphanumeric SKU/color (Propper-style), not just numeric (Condor/Rothco)
    public void TryParsePhotoFileName_MainPhotoName_ParsesWithNoExtraPhotoNumber(string fileName)
    {
        var ok = ProductPhotosController.TryParsePhotoFileName(fileName, out var parsed, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(parsed.ExtraPhotoNumber);
    }

    [Fact]
    public void TryParsePhotoFileName_MainPhotoName_ExtractsVendorSkuAndColorCode()
    {
        var ok = ProductPhotosController.TryParsePhotoFileName("101119-029.jpg", out var parsed, out _);

        Assert.True(ok);
        Assert.Equal("101119", parsed.VendorSku);
        Assert.Equal("029", parsed.VendorColorCode);
        Assert.Equal(".jpg", parsed.Extension);
    }

    [Theory]
    [InlineData("101119-029-2.jpg", 2)]
    [InlineData("101119-029-20.jpg", 20)] // upper end of the allowed range
    [InlineData("101119-029-02.jpg", 2)] // leading zero still parses as 2
    [InlineData("101119-029-9.JPEG", 9)] // extension casing ignored here too
    public void TryParsePhotoFileName_ExtraPhotoName_ParsesTheNumberAndStillExtractsSkuAndColor(string fileName, int expectedNumber)
    {
        var ok = ProductPhotosController.TryParsePhotoFileName(fileName, out var parsed, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expectedNumber, parsed.ExtraPhotoNumber);
        Assert.Equal("101119", parsed.VendorSku);
        Assert.Equal("029", parsed.VendorColorCode);
    }

    [Theory]
    [InlineData("101119-029-zip-pocket.jpg")] // the exact bug-report example — an extra word, not a photo number
    [InlineData("101119-029-front.jpg")] // any other non-numeric word
    [InlineData("101119-029-a.jpg")] // even a single non-numeric character
    public void TryParsePhotoFileName_NonNumericSuffix_IsRejected_NeverTreatedAsMainOrExtra(string fileName)
    {
        var ok = ProductPhotosController.TryParsePhotoFileName(fileName, out var parsed, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        // Must never be silently matched to the main photo (ExtraPhotoNumber null is what a real
        // main photo ALSO produces — "ok" being false is what actually distinguishes "rejected"
        // from "main photo", so asserting ok is false above is the real guard; this just confirms
        // parsed is left at its default, unused state too).
        Assert.Null(parsed.ExtraPhotoNumber);
    }

    [Theory]
    [InlineData("101119-029-1.jpg")] // below the allowed range — the main photo already has no suffix
    [InlineData("101119-029-0.jpg")]
    [InlineData("101119-029-21.jpg")] // above the allowed range
    [InlineData("101119-029-99.jpg")]
    public void TryParsePhotoFileName_NumericSuffixOutOfRange_IsRejectedWithARangeSpecificMessage(string fileName)
    {
        var ok = ProductPhotosController.TryParsePhotoFileName(fileName, out _, out var error);

        Assert.False(ok);
        Assert.Contains("2", error);
        Assert.Contains("20", error);
    }

    [Theory]
    [InlineData("101119-029.gif")] // unsupported extension — not even in the regex's own alternation
    [InlineData("101119-029-2-extra.jpg")] // trailing text after a valid-looking number
    [InlineData("101119-029-029.jpg")] // more than 2 digits
    [InlineData("A-BC.jpg")] // SKU shorter than the minimum 2 characters
    [InlineData("AB-C.jpg")] // color code shorter than the minimum 2 characters
    [InlineData("not-a-vendor-filename-at-all")] // no extension at all
    public void TryParsePhotoFileName_DoesNotMatchEitherShapeAtAll_IsRejected(string fileName)
    {
        var ok = ProductPhotosController.TryParsePhotoFileName(fileName, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    // --- DeleteExtraPhoto (Admin -> Products -> Edit's "Color Photos" Delete button) ---------

    private static (ApplicationDbContext Context, SqliteConnection Connection) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    private static ProductPhotosController CreateController(ApplicationDbContext context) =>
        new(context, new ConfigurationBuilder().Build(), new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions())));

    private static (Product Product, ProductColor Color, ProductMedia Main, ProductMedia Extra) SeedProductWithMainAndExtraPhoto(ApplicationDbContext context)
    {
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

        var main = new ProductMedia
        {
            ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image",
            MediaUrl = "https://blob.example/product-photos/f5519-001.webp", IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true
        };
        var extra = new ProductMedia
        {
            ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image",
            MediaUrl = "https://blob.example/product-photos/f5519-001-2.webp", IsColorMain = false, DisplayOrder = 2, ShowInGallery = true, Active = true
        };
        context.ProductMedia.AddRange(main, extra);
        context.SaveChanges();

        return (product, productColor, main, extra);
    }

    [Fact]
    public async Task DeleteExtraPhoto_ExtraPhoto_RemovesItAndLeavesMainUntouched()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (product, _, main, extra) = SeedProductWithMainAndExtraPhoto(context);
        var controller = CreateController(context);

        var result = await controller.DeleteExtraPhoto(extra.Id, product.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Null(await context.ProductMedia.FindAsync(extra.Id));
        var reloadedMain = await context.ProductMedia.FindAsync(main.Id);
        Assert.NotNull(reloadedMain);
        Assert.True(reloadedMain!.IsColorMain);
        Assert.Equal(1, reloadedMain.DisplayOrder);
    }

    [Fact]
    public async Task DeleteExtraPhoto_MainPhoto_RefusesToDeleteIt()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (product, _, main, extra) = SeedProductWithMainAndExtraPhoto(context);
        var controller = CreateController(context);

        await controller.DeleteExtraPhoto(main.Id, product.Id);

        // Both rows still present — the main photo was never removed, and the untouched extra
        // confirms nothing else was deleted along the way.
        Assert.NotNull(await context.ProductMedia.FindAsync(main.Id));
        Assert.NotNull(await context.ProductMedia.FindAsync(extra.Id));
    }

    [Fact]
    public async Task DeleteExtraPhoto_RenumbersTheRemainingExtrasAfterDeletingOneInTheMiddle()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var (product, productColor, _, secondPhoto) = SeedProductWithMainAndExtraPhoto(context);
        var thirdPhoto = new ProductMedia
        {
            ProductId = product.Id, ProductColorId = productColor.Id, MediaScope = "Color", MediaType = "Image",
            MediaUrl = "https://blob.example/product-photos/f5519-001-3.webp", IsColorMain = false, DisplayOrder = 3, ShowInGallery = true, Active = true
        };
        context.ProductMedia.Add(thirdPhoto);
        context.SaveChanges();
        var controller = CreateController(context);

        await controller.DeleteExtraPhoto(secondPhoto.Id, product.Id);

        var reloadedThird = await context.ProductMedia.FindAsync(thirdPhoto.Id);
        Assert.NotNull(reloadedThird);
        Assert.Equal(2, reloadedThird!.DisplayOrder); // closed the gap left by deleting photo -2
    }
}
