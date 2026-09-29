using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Views/Products/Index.cshtml's Color and Price filters, and how they drive each product
/// card (Views/Shared/_ProductCard.cshtml + wwwroot/js/product-card-swatches.js).
///
/// "24-7 Agility Pant" (Data/SeedData.cs) is real production data used specifically to cover
/// this: priced 530.25 for most colors, but Ranger Green undercuts it at 472.50 — the only
/// seeded product whose price actually varies by color, so it's the one that can prove the
/// listing page filters/sorts/displays by VARIANT price, not just Product.Price.
///
/// "?search=24-7" scopes every test to just this one product (it's the only seeded item with
/// "24-7" anywhere in its name/Sku) rather than relying on pagination/sort order to put it on
/// page 1 — a plain product-name search doesn't trigger the exact-match-redirect added for
/// variant Sku/Barcode searches (PR #153), so this always lands on the ordinary listing page.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ListingColorPriceFilterTests : E2ETestBase
{
    public ListingColorPriceFilterTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private ILocator AgilityPantCard =>
        Page.Locator(".product-card").Filter(new LocatorFilterOptions { HasText = "24-7 Agility Pant" });

    [Fact]
    public async Task CheckingGreenOliveFilter_SwitchesCardToRangerGreen_PriceAndSwatch()
    {
        await Page.GotoAsync(Url("Products?search=24-7"));
        await Assertions.Expect(AgilityPantCard).ToBeVisibleAsync();
        await Assertions.Expect(AgilityPantCard.Locator(".price")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("530\\.25"));

        await Page.Locator("#colorFacetList input[type='checkbox'][value='green-olive']").CheckAsync();
        await Page.WaitForURLAsync(url => url.Contains("colors=green-olive"));

        await Assertions.Expect(AgilityPantCard).ToBeVisibleAsync();
        await Assertions.Expect(AgilityPantCard.Locator(".price")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("472\\.5"));
        await Assertions.Expect(AgilityPantCard.Locator(".product-card-swatch[data-color='Ranger Green']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));

        // Unchecking returns the card to its default (base-priced) color.
        await Page.Locator("#colorFacetList input[type='checkbox'][value='green-olive']").UncheckAsync();
        await Page.WaitForURLAsync(url => !url.Contains("colors=green-olive"));
        await Assertions.Expect(AgilityPantCard.Locator(".price")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("530\\.25"));
    }

    [Fact]
    public async Task ClickingCardWithColorFilterActive_OpensProductPageWithThatColorPreselected()
    {
        await Page.GotoAsync(Url("Products?search=24-7&colors=green-olive"));
        await AgilityPantCard.Locator("h3 a.product-card-link").ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("24-7-agility-pant"));
        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Ranger Green");
    }

    [Fact]
    public async Task ClickingSwatchOnCard_ChangesImagePriceAndFollowsLinkToThatColor()
    {
        await Page.GotoAsync(Url("Products?search=24-7"));

        var swatch = AgilityPantCard.Locator(".product-card-swatch[data-color='Ranger Green']");
        var image = AgilityPantCard.Locator(".card-product-image:not(.card-product-image-hover)");
        var defaultImageSrc = await image.GetAttributeAsync("src");

        await swatch.HoverAsync();
        await Assertions.Expect(AgilityPantCard.Locator(".price")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("472\\.5"));
        var hoveredImageSrc = await image.GetAttributeAsync("src");
        Assert.NotEqual(defaultImageSrc, hoveredImageSrc);

        await swatch.ClickAsync();
        await AgilityPantCard.Locator("h3 a.product-card-link").ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("24-7-agility-pant"));
        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Ranger Green");
    }

    [Fact]
    public async Task PriceFilter_ScopedToSelectedColor_ExcludesProduct_WhenRangerGreenPriceOutOfRange()
    {
        // 530-540 covers the base price every OTHER color has, but not Ranger Green's 472.50 —
        // with Green/Olive as the only selected color, the product must be excluded entirely.
        await Page.GotoAsync(Url("Products?search=24-7&colors=green-olive&minPrice=530&maxPrice=540"));

        await Assertions.Expect(AgilityPantCard).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task PriceFilter_ScopedToSelectedColor_IncludesProduct_WhenRangerGreenPriceInRange()
    {
        await Page.GotoAsync(Url("Products?search=24-7&colors=green-olive&minPrice=470&maxPrice=480"));

        await Assertions.Expect(AgilityPantCard).ToBeVisibleAsync();
        await Assertions.Expect(AgilityPantCard.Locator(".price")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("472\\.5"));
    }

    [Fact]
    public async Task PriceFilter_NoColorSelected_StillMatchesViaTheOneCheapVariant()
    {
        // No color filter: the product must still be findable via its Ranger Green variant
        // alone at 470-480, even though every other color is priced well outside that range.
        await Page.GotoAsync(Url("Products?search=24-7&minPrice=470&maxPrice=480"));

        await Assertions.Expect(AgilityPantCard).ToBeVisibleAsync();
    }
}
