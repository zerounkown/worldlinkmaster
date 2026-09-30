using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Bug report: product F5519 "Tactical Boonie" has 2 ProductMedia images per color (uploaded via
/// Admin -&gt; Product Photos) — Black main + Black bottom-view, Khaki main + Khaki bottom-view —
/// but the PDP gallery only ever showed ONE image per color (the main one) and the arrows jumped
/// straight from one color's single photo to the next color's, skipping every "-2" image
/// entirely.
///
/// Root cause: Views/Products/Details.cshtml built colorThumbItems (the thumbnail strip data for
/// any product with more than one ProductColor — see _PdpColorGallery.cshtml) from exactly one
/// representative photo per color, discarding every OTHER real ProductMedia row that color had.
/// galleryByColorId (the per-color "all of this color's own photos" data) already had everything,
/// but colorThumbItems took priority over it whenever present, which was always true for any
/// multi-color product. Fixed by building colorThumbItems from every one of a color's own real
/// photos (main first, by DisplayOrder — see ProductMediaOrderingHelper), concatenated color by
/// color in Model.ProductColors order, instead of just the first one. Since wwwroot/js/product-
/// gallery.js's thumbnail rendering, main-image arrows, and lightbox were all already generic over
/// "a flat list of items each tagged with a colorId" (never assumed one item per color), none of
/// that needed to change — except renderLightboxImage, which never called syncColorFromThumb at
/// all (a separate, pre-existing gap this fix also closes, now that lightbox navigation crosses
/// color boundaries far more often).
///
/// A product with only one ProductMedia (or none at all — the SwatchImageUrl/variant-image
/// fallback) per color, like the real "24-7 Agility Pant", is unaffected: colorThumbItems still
/// gets exactly one item for that color, identical to before this fix.
/// VariantPreselectGallerySyncTests' seeded product (ProductColors, but no ProductMedia rows at
/// all) already covers exactly that shape end to end — its own
/// OpeningWithVariant_PreselectsColorAndWaistLength_AndSubsequentColorChangeUpdatesGallery test
/// passing unchanged after this fix IS that regression guard, so this file doesn't duplicate it.
///
/// No seeded product in Data/SeedData.cs has multiple ProductMedia rows for the same color — this
/// class seeds one directly into the running app's own Postgres schema (via
/// E2EWebAppFactory.ConnectionString), same pattern as VariantPreselectGallerySyncTests.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class PdpGalleryAllColorImagesTests : E2ETestBase
{
    private const string ProductSlug = "e2e-pdp-gallery-all-images-boonie";
    private const string BlackMainUrl = "https://example.com/e2e-pdp-gallery-black-main.jpg";
    private const string BlackBottomUrl = "https://example.com/e2e-pdp-gallery-black-bottom.jpg";
    private const string KhakiMainUrl = "https://example.com/e2e-pdp-gallery-khaki-main.jpg";
    private const string KhakiBottomUrl = "https://example.com/e2e-pdp-gallery-khaki-bottom.jpg";

    private int _khakiVariantId;

    public PdpGalleryAllColorImagesTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _khakiVariantId = await SeedProductAsync();
    }

    private async Task<int> SeedProductAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(App.ConnectionString).Options;
        await using var context = new ApplicationDbContext(options);

        var existingKhakiVariant = await context.Products
            .Where(p => p.Slug == ProductSlug)
            .SelectMany(p => p.Variants)
            .Where(v => v.Barcode == "E2E-PGA-KHAKI")
            .Select(v => v.Id)
            .FirstOrDefaultAsync();
        if (existingKhakiVariant != 0)
        {
            return existingKhakiVariant; // Already seeded by a previous run in this shared schema.
        }

        var category = await context.Categories.FirstAsync();
        var merchant = await context.Merchants.FirstAsync();

        var blackColor = await context.Colors.FirstOrDefaultAsync(c => c.Name == "Black")
            ?? new Color { Name = "Black", HexCode = "#000000" };
        if (blackColor.Id == 0) context.Colors.Add(blackColor);
        var khakiColor = await context.Colors.FirstOrDefaultAsync(c => c.Name == "Khaki")
            ?? new Color { Name = "Khaki", HexCode = "#C3B091" };
        if (khakiColor.Id == 0) context.Colors.Add(khakiColor);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "E2E PDP Gallery All Images Boonie",
            Slug = ProductSlug,
            Sku = "E2E-PGA-001",
            Price = 40m,
            StockQuantity = 20,
            CategoryId = category.Id,
            MerchantId = merchant.Id,
            IsPublished = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var blackProductColor = new ProductColor { Code = "E2E-PGA-BLACK", ProductId = product.Id, ColorId = blackColor.Id, DisplayOrder = 1, DefaultColor = true, Active = true };
        var khakiProductColor = new ProductColor { Code = "E2E-PGA-KHAKI", ProductId = product.Id, ColorId = khakiColor.Id, DisplayOrder = 2, DefaultColor = false, Active = true };
        context.ProductColors.AddRange(blackProductColor, khakiProductColor);
        await context.SaveChangesAsync();

        var blackVariant = new ProductVariant { ProductId = product.Id, ColorId = blackColor.Id, ProductColorId = blackProductColor.Id, Sku = "E2E-PGA-001-BLK", Barcode = "E2E-PGA-BLACK", StockQuantity = 10, Active = true };
        var khakiVariant = new ProductVariant { ProductId = product.Id, ColorId = khakiColor.Id, ProductColorId = khakiProductColor.Id, Sku = "E2E-PGA-001-KHK", Barcode = "E2E-PGA-KHAKI", StockQuantity = 10, Active = true };
        context.ProductVariants.AddRange(blackVariant, khakiVariant);
        await context.SaveChangesAsync();

        // Two real ProductMedia rows per color — IsColorMain/DisplayOrder set exactly the way
        // ProductMediaOrderingHelper.NormalizeColorMediaOrdering would leave them after a real
        // Admin -> Product Photos upload (main = DisplayOrder 1, the "-2" extra angle = 2).
        context.ProductMedia.AddRange(
            new ProductMedia { ProductId = product.Id, ProductColorId = blackProductColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = BlackMainUrl, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true },
            new ProductMedia { ProductId = product.Id, ProductColorId = blackProductColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = BlackBottomUrl, IsColorMain = false, DisplayOrder = 2, ShowInGallery = true, Active = true },
            new ProductMedia { ProductId = product.Id, ProductColorId = khakiProductColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = KhakiMainUrl, IsColorMain = true, DisplayOrder = 1, ShowInGallery = true, Active = true },
            new ProductMedia { ProductId = product.Id, ProductColorId = khakiProductColor.Id, MediaScope = "Color", MediaType = "Image", MediaUrl = KhakiBottomUrl, IsColorMain = false, DisplayOrder = 2, ShowInGallery = true, Active = true });
        await context.SaveChangesAsync();

        return khakiVariant.Id;
    }

    private async Task<ProductDetailPage> OpenProductAsync()
    {
        await Page.GotoAsync(Url($"Products/Details?slug={ProductSlug}"));
        var detail = new ProductDetailPage(Page);
        await detail.MainImage.WaitForAsync();
        return detail;
    }

    [Fact]
    public async Task ThumbnailStrip_IncludesAllFourImages_GroupedByColor_MainFirstWithinEachColor()
    {
        var detail = await OpenProductAsync();

        await Assertions.Expect(detail.Thumbs).ToHaveCountAsync(4);
        var fullUrls = await detail.Thumbs.EvaluateAllAsync<string[]>("els => els.map(el => el.getAttribute('data-full'))");
        Assert.Equal(new[] { BlackMainUrl, BlackBottomUrl, KhakiMainUrl, KhakiBottomUrl }, fullUrls);

        // Opens on Black (the default color) main image — first thumbnail active, not some
        // arbitrary one.
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", BlackMainUrl);
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", BlackMainUrl);
    }

    [Fact]
    public async Task MainImageArrows_StepThroughEveryImage_OnlyChangingColorAtTheColorBoundary()
    {
        var detail = await OpenProductAsync();

        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");

        // Black main -> Black bottom: same color, no color-selection change.
        await detail.MainImageArrowNext.ClickAsync();
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", BlackBottomUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");

        // Black bottom -> Khaki main: crosses the color boundary — the actual bug (arrows used
        // to jump straight from one color's ONLY photo to the next color's, skipping "-2"
        // entirely; now there's a same-color step first, and the color only changes here).
        await detail.MainImageArrowNext.ClickAsync();
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", KhakiMainUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", KhakiMainUrl);

        // Khaki main -> Khaki bottom: same color again.
        await detail.MainImageArrowNext.ClickAsync();
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", KhakiBottomUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");

        // Wrap-around: Khaki bottom (last) -> Black main (first), color changes back.
        await detail.MainImageArrowNext.ClickAsync();
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", BlackMainUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");
    }

    [Fact]
    public async Task ClickingASecondaryThumbnail_SelectsThatImagesColor_AndSwatchClick_JumpsBackToMainImage()
    {
        var detail = await OpenProductAsync();

        // Click the Khaki bottom-view thumbnail directly (not the arrow) — the 4th thumb.
        await detail.Thumbs.Nth(3).ClickAsync();
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", KhakiBottomUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");

        // Now click the Black swatch directly — must jump to Black's MAIN image, not stay on
        // whatever angle was last shown or land on some other Black photo.
        await Page.Locator(".color-swatch[title='Black']").ClickAsync(new LocatorClickOptions { Force = true });
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", BlackMainUrl);
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", BlackMainUrl);
    }

    [Fact]
    public async Task LightboxArrowsAndKeyboard_AlsoSyncSelectedColor_WhenCrossingAColorBoundary()
    {
        var detail = await OpenProductAsync();
        await detail.OpenLightboxAsync();
        await Assertions.Expect(detail.LightboxCounter).ToHaveTextAsync("1 / 4");

        // Lightbox Next: Black main -> Black bottom (same color) -> Khaki main (color changes).
        // Before this fix, renderLightboxImage never called syncColorFromThumb at all, so the
        // page's own selected-color state (label / swatch / price) never updated no matter how
        // far the lightbox itself navigated.
        await detail.LightboxNext.ClickAsync();
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");
        await detail.LightboxNext.ClickAsync();
        await Assertions.Expect(detail.LightboxImage).ToHaveAttributeAsync("src", KhakiMainUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");

        // Keyboard ArrowRight inside the lightbox does the same thing (Khaki main -> Khaki
        // bottom, still Khaki; one more wraps to Black main).
        await Page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(detail.LightboxImage).ToHaveAttributeAsync("src", KhakiBottomUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");
        await Page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(detail.LightboxImage).ToHaveAttributeAsync("src", BlackMainUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");

        // Closing the lightbox leaves the underlying page showing the same image/color it was
        // last navigated to inside the lightbox, not whatever it was opened on.
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", BlackMainUrl);
        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Black");
    }

    [Fact]
    public async Task OpeningWithVariant_PreselectsKhaki_AndOpensOnKhakisMainImage_NotWhicheverVariantMatched()
    {
        // _khakiVariantId has no ProductMedia/gallery meaning of its own (it's just a size/SKU
        // record) — the gallery must open on Khaki's MAIN photo regardless, never something
        // resolved from the matched variant itself.
        await Page.GotoAsync(Url($"Products/Details?slug={ProductSlug}&variant={_khakiVariantId}"));
        var detail = new ProductDetailPage(Page);
        await detail.MainImage.WaitForAsync();

        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync("Khaki");
        await Assertions.Expect(detail.MainImage).ToHaveAttributeAsync("src", KhakiMainUrl);
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", KhakiMainUrl);
    }
}
