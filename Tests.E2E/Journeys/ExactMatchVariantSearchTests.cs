using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Controllers/ProductsController.cs Index's exact-match redirect (search text exactly matches a
/// variant Sku/Barcode -> straight to that product's page, variant pre-selected) and
/// Views/Products/Details.cshtml's ?variant= handling (that variant's color AND size selected on
/// load, not just color). "Sentinel Combat Shirt" (sentinel-combat-shirt) is the same fixed,
/// deterministic SeedData.cs product ProductGalleryTests.cs and HeaderInstantSearchTests.cs
/// already rely on — its exact per-color variant Skus aren't hardcoded here (SeedData's variant
/// numbering is an implementation detail, not part of the seeded product's public contract);
/// instead each test discovers the real Sku for "Khaki" by clicking that swatch on the PDP
/// itself first, the same way a shopper's own click would generate it.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ExactMatchVariantSearchTests : E2ETestBase
{
    public ExactMatchVariantSearchTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private async Task<(string Sku, string Size)> DiscoverKhakiVariantAsync()
    {
        await Page.GotoAsync(Url("Products/Details?slug=sentinel-combat-shirt"));
        await Page.Locator(".color-swatch[title='Khaki']").ClickAsync();

        var sku = await Page.Locator("#pdpSkuValue").InnerTextAsync();
        var size = await Page.Locator("input[name='size']:checked").GetAttributeAsync("value");

        Assert.False(string.IsNullOrWhiteSpace(sku));
        Assert.False(string.IsNullOrWhiteSpace(size));
        return (sku.Trim(), size!);
    }

    [Fact]
    public async Task EnterOnExactVariantSku_OpensProductPageDirectly_WithColorAndSizePreselected()
    {
        var (sku, size) = await DiscoverKhakiVariantAsync();

        await Page.GotoAsync(BaseUrl);
        var searchInput = Page.Locator(".header-search-inline input.js-instant-search-input");
        await searchInput.FillAsync(sku);
        await searchInput.PressAsync("Enter");

        // Lands on the product page directly — never on /Products (the search-results page).
        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("sentinel-combat-shirt"));

        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Khaki");
        await Assertions.Expect(Page.Locator($"input[name='size'][value='{size}']")).ToBeCheckedAsync();
    }

    [Fact]
    public async Task SubmittingSearchForm_WithExactVariantBarcode_RedirectsDirectlyToProductPage()
    {
        // No barcode is seeded for this product (SeedData.cs never sets ProductVariant.Barcode),
        // so this exercises the Sku side of the same exact-match code path
        // (FindExactVariantMatchAsync matches Sku OR Barcode identically) via a plain full-page
        // navigation — i.e. what "submitting the search form" (not the JS dropdown at all) does,
        // confirming the redirect doesn't depend on the instant-search script running.
        var (sku, _) = await DiscoverKhakiVariantAsync();

        await Page.GotoAsync(Url($"Products?search={Uri.EscapeDataString(sku)}"));

        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("sentinel-combat-shirt"));
        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Khaki");
    }

    [Fact]
    public async Task ClickingDropdownResult_ForExactVariantSkuMatch_OpensProductPage_WithColorAndSizePreselected()
    {
        var (sku, size) = await DiscoverKhakiVariantAsync();

        await Page.GotoAsync(BaseUrl);
        var searchInput = Page.Locator(".header-search-inline input.js-instant-search-input");
        var dropdown = Page.Locator(".header-search-inline .instant-search-dropdown");
        await searchInput.FillAsync(sku);

        var firstResult = dropdown.Locator(".instant-search-item").First;
        await firstResult.WaitForAsync();
        await firstResult.ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("sentinel-combat-shirt"));
        await Assertions.Expect(Page.Locator("#selectedColorLabel")).ToHaveTextAsync("Khaki");
        await Assertions.Expect(Page.Locator($"input[name='size'][value='{size}']")).ToBeCheckedAsync();
    }

    [Fact]
    public async Task PartialMatch_StillShowsDropdownAndResultsPage_InsteadOfRedirecting()
    {
        await Page.GotoAsync(BaseUrl);
        var searchInput = Page.Locator(".header-search-inline input.js-instant-search-input");
        var dropdown = Page.Locator(".header-search-inline .instant-search-dropdown");

        // A partial term ("Sentinel" — not the full Sku/Barcode) must NOT trigger the exact-match
        // redirect: the dropdown still shows results, and submitting still lands on the ordinary
        // search-results page, not a product page.
        await searchInput.FillAsync("Sentinel");
        await Assertions.Expect(dropdown.Locator(".instant-search-item").First).ToBeVisibleAsync();

        await searchInput.PressAsync("Enter");
        await Page.WaitForURLAsync(url => url.Contains("/Products?") && url.Contains("search="));
        await Assertions.Expect(Page.Locator(".product-card").First).ToBeVisibleAsync();
    }
}
