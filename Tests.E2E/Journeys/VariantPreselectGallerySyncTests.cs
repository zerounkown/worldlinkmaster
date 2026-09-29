using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Regression coverage for a bug reported after PR #153 (?variant= pre-selection): landing on a
/// product page via ?variant= correctly pre-selected the color label and (for the one real
/// "Trouser Waist x Length" product) the waist/length, but selecting a DIFFERENT color afterward
/// left the main image and thumbnails stuck on the ?variant= color, even though the "COLOR: ..."
/// label updated correctly.
///
/// Root cause: Views/Products/_PdpColorGallery.cshtml (the multi-color gallery panel partial)
/// independently re-derived its own defaultProductColor straight from the raw ?color= query
/// string, never learning about ?variant= at all — so it could open on a different color than
/// the buy-box's own swatches/size pickers whenever ?variant= resolved to something other than
/// the product's own DefaultColor. Fixed by having Details.cshtml pass its own (correctly
/// ?variant=-aware) defaultProductColor into the partial via ViewData instead of letting it
/// recompute a second, divergent one. wwwroot/js/product-gallery.js's color-change handler was
/// also reordered so the gallery (main image + thumbnails) updates first and unconditionally,
/// before the separate size-picker rebuild — belt and braces, so a data gap in one color's size
/// options can never again block the photo update for an otherwise-healthy color change.
///
/// No seeded product in Data/SeedData.cs uses the "Trouser Waist x Length" (TR-WL) size group —
/// this class seeds one directly into the running app's own Postgres schema (via
/// E2EWebAppFactory.ConnectionString), so that path gets exercised too, not just the plain-size
/// "Sentinel Combat Shirt" products the rest of the suite already covers.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class VariantPreselectGallerySyncTests : E2ETestBase
{
    private const string ProductSlug = "e2e-variant-gallery-sync-pants";
    private int _khakiVariantId;

    public VariantPreselectGallerySyncTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
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
            .Where(v => v.Barcode == "E2E-VGS-KHAKI-2832")
            .Select(v => v.Id)
            .FirstOrDefaultAsync();
        if (existingKhakiVariant != 0)
        {
            return existingKhakiVariant; // Already seeded by a previous run in this shared schema.
        }

        var category = await context.Categories.FirstAsync();
        var merchant = await context.Merchants.FirstAsync();

        var sizeGroup = new SizeGroup { Code = "TR-WL", NameEn = "Trouser Waist x Length" };
        context.SizeGroups.Add(sizeGroup);
        await context.SaveChangesAsync();

        var size2830 = new Size { Label = "28x30", SizeGroupId = sizeGroup.Id, SortOrder = 1 };
        var size2832 = new Size { Label = "28x32", SizeGroupId = sizeGroup.Id, SortOrder = 2 };
        context.Sizes.AddRange(size2830, size2832);
        await context.SaveChangesAsync();

        var khakiColor = await context.Colors.FirstOrDefaultAsync(c => c.Name == "Khaki");
        if (khakiColor == null)
        {
            khakiColor = new Color { Name = "Khaki", HexCode = "#C3B091" };
            context.Colors.Add(khakiColor);
        }
        var blackColor = await context.Colors.FirstOrDefaultAsync(c => c.Name == "Black");
        if (blackColor == null)
        {
            blackColor = new Color { Name = "Black", HexCode = "#000000" };
            context.Colors.Add(blackColor);
        }
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "E2E Variant Gallery Sync Pants",
            Slug = ProductSlug,
            Sku = "E2E-VGS-001",
            Price = 200m,
            StockQuantity = 20,
            CategoryId = category.Id,
            MerchantId = merchant.Id,
            IsPublished = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var khakiProductColor = new ProductColor { Code = "E2E-VGS-KHAKI", ProductId = product.Id, ColorId = khakiColor.Id, DisplayOrder = 1, DefaultColor = true, Active = true };
        var blackProductColor = new ProductColor { Code = "E2E-VGS-BLACK", ProductId = product.Id, ColorId = blackColor.Id, DisplayOrder = 2, DefaultColor = false, Active = true };
        context.ProductColors.AddRange(khakiProductColor, blackProductColor);
        await context.SaveChangesAsync();

        // Distinct, real-looking (never actually fetched) per-color image URLs so the test can
        // assert the main image genuinely changed, not just that *a* change happened.
        var khakiVariant2830 = new ProductVariant { ProductId = product.Id, ColorId = khakiColor.Id, ProductColorId = khakiProductColor.Id, SizeId = size2830.Id, Sku = "E2E-VGS-001-KHK-2830", Barcode = "E2E-VGS-KHAKI-2830", StockQuantity = 5, Active = true, ImageUrl = "https://example.com/e2e-vgs-khaki.jpg" };
        var khakiVariant2832 = new ProductVariant { ProductId = product.Id, ColorId = khakiColor.Id, ProductColorId = khakiProductColor.Id, SizeId = size2832.Id, Sku = "E2E-VGS-001-KHK-2832", Barcode = "E2E-VGS-KHAKI-2832", StockQuantity = 5, Active = true, ImageUrl = "https://example.com/e2e-vgs-khaki.jpg" };
        var blackVariant2830 = new ProductVariant { ProductId = product.Id, ColorId = blackColor.Id, ProductColorId = blackProductColor.Id, SizeId = size2830.Id, Sku = "E2E-VGS-001-BLK-2830", Barcode = "E2E-VGS-BLACK-2830", StockQuantity = 5, Active = true, ImageUrl = "https://example.com/e2e-vgs-black.jpg" };
        var blackVariant2832 = new ProductVariant { ProductId = product.Id, ColorId = blackColor.Id, ProductColorId = blackProductColor.Id, SizeId = size2832.Id, Sku = "E2E-VGS-001-BLK-2832", Barcode = "E2E-VGS-BLACK-2832", StockQuantity = 5, Active = true, ImageUrl = "https://example.com/e2e-vgs-black.jpg" };
        context.ProductVariants.AddRange(khakiVariant2830, khakiVariant2832, blackVariant2830, blackVariant2832);
        await context.SaveChangesAsync();

        return khakiVariant2832.Id; // Khaki, waist 28, length 32 — matches the reported barcode's combo.
    }

    [Fact]
    public async Task OpeningWithVariant_PreselectsColorAndWaistLength_AndSubsequentColorChangeUpdatesGallery()
    {
        await Page.GotoAsync(Url($"Products/Details?slug={ProductSlug}&variant={_khakiVariantId}"));

        // ?variant= pre-selection: color label and BOTH waist and length, not just color.
        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Khaki");
        await Assertions.Expect(Page.Locator("#selectedWaistLabel")).ToHaveTextAsync("28");
        await Assertions.Expect(Page.Locator("#selectedLengthLabel")).ToHaveTextAsync("32");

        var khakiImageSrc = await Page.Locator("#mainProductImage").GetAttributeAsync("src");
        Assert.Contains("khaki", khakiImageSrc, StringComparison.OrdinalIgnoreCase);

        // The actual regression: selecting a different color after landing via ?variant= must
        // still update the main image and thumbnails, exactly like it does on a normal page load.
        await Page.Locator(".color-swatch[title='Black']").ClickAsync();

        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Black");
        await Assertions.Expect(Page.Locator("#mainProductImage")).Not.ToHaveAttributeAsync("src", khakiImageSrc!);
        var blackImageSrc = await Page.Locator("#mainProductImage").GetAttributeAsync("src");
        Assert.Contains("black", blackImageSrc, StringComparison.OrdinalIgnoreCase);

        await Assertions.Expect(Page.Locator(".product-thumb.active")).ToHaveAttributeAsync("data-full", blackImageSrc!);

        // Size picking still works normally afterward too, not just color.
        await Assertions.Expect(Page.Locator("input[name='waistOption']:checked")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("input[name='lengthOption']:checked")).ToHaveCountAsync(1);
    }
}
