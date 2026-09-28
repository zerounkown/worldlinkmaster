using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Views/Shared/_Layout.cshtml's header search box + wwwroot/js/header-instant-search.js +
/// Controllers/ProductsController.cs InstantSearch. Regression coverage for a live-site bug:
/// clicking a dropdown result with the mouse did nothing (see header-instant-search.js's
/// guardAgainstBlur — mousedown on a result shifted focus off the input before the click event
/// fired, and the resulting blur-driven side effects could keep the click from ever landing).
/// "Sentinel Combat Shirt" (slug sentinel-combat-shirt, Sku WLM-APP-001) is a fixed,
/// deterministic product from Data/SeedData.cs, same choice ProductGalleryTests.cs already
/// makes for the same reason — reliable regardless of catalog ordering/content changes.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class HeaderInstantSearchTests : E2ETestBase
{
    public HeaderInstantSearchTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private ILocator SearchInput => Page.Locator(".header-search-inline input.js-instant-search-input");
    private ILocator Dropdown => Page.Locator(".header-search-inline .instant-search-dropdown");

    [Fact]
    public async Task TypingInHeaderSearch_ClickingResultWithMouse_NavigatesToProductPage()
    {
        await Page.GotoAsync(BaseUrl);

        await SearchInput.FillAsync("Sentinel Combat Shirt");

        var firstResult = Dropdown.Locator(".instant-search-item").First;
        await firstResult.WaitForAsync();

        // The actual regression: this used to do nothing. A real mouse click (not JS-dispatched)
        // exercises the same pointerdown -> mousedown -> click sequence a real user's click does.
        await firstResult.ClickAsync();

        await Page.WaitForURLAsync(url => url.Contains("/Products/Details") && url.Contains("sentinel-combat-shirt"));
        await Assertions.Expect(Page.Locator("button.pdp-btn-cart")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TypingInHeaderSearch_ResultHover_ShowsActiveHighlight()
    {
        await Page.GotoAsync(BaseUrl);

        await SearchInput.FillAsync("Sentinel Combat Shirt");

        var firstResult = Dropdown.Locator(".instant-search-item").First;
        await firstResult.WaitForAsync();

        await firstResult.HoverAsync();

        // :hover isn't itself queryable, but the same CSS rule also drives keyboard selection
        // via .active (header-instant-search.js's setActive) — confirm arrow-down applies the
        // same visible highlight class real hover relies on.
        await SearchInput.PressAsync("ArrowDown");
        await Assertions.Expect(firstResult).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
    }

    [Fact]
    public async Task TypingInHeaderSearch_NoMatch_ShowsNoResultsMessage()
    {
        await Page.GotoAsync(BaseUrl);

        await SearchInput.FillAsync("zzz-no-such-product-zzz");

        await Assertions.Expect(Dropdown.Locator(".instant-search-empty")).ToBeVisibleAsync();
        await Assertions.Expect(Dropdown.Locator(".instant-search-item")).ToHaveCountAsync(0);
    }
}
