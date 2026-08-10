using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class ProductBrowsingTests : E2ETestBase
{
    public ProductBrowsingTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task Browse_ApplyFilter_OpenProductDetail()
    {
        await Page.GotoAsync(Url("Products"));
        await Assertions.Expect(Page.Locator(".product-card").First).ToBeVisibleAsync();

        // Views/Products/Index.cshtml's filter checkboxes auto-submit the GET form on change
        // (wwwroot/js/site.js) — apply the first available color swatch facet. (Not brand: SeedData
        // never seeds any Brands, so BrandFacets is always empty and that panel never renders.)
        var colorCheckbox = Page.Locator(".filter-swatch-row input[type='checkbox']").First;
        await colorCheckbox.WaitForAsync();
        await colorCheckbox.CheckAsync();
        await Page.WaitForURLAsync(url => url.Contains("colors="));
        await Assertions.Expect(Page.Locator(".product-card").First).ToBeVisibleAsync();

        var firstCard = Page.Locator("a.product-card-tilt").First;
        await firstCard.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("slug="));

        await Assertions.Expect(Page.Locator("button.pdp-btn-cart")).ToBeVisibleAsync();
    }
}
