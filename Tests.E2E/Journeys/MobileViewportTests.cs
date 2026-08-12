using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class MobileViewportTests : E2ETestBase
{
    protected override ViewportSize ViewportSize => new() { Width = 375, Height = 667 };

    public MobileViewportTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private async Task AssertNoHorizontalOverflowAsync()
    {
        var overflowPx = await Page.EvaluateAsync<int>(
            "document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.True(overflowPx <= 0, $"Page overflows horizontally by {overflowPx}px at 375px viewport.");
    }

    [Fact]
    public async Task Homepage_At375px_NoHorizontalOverflow_KeyElementsVisible()
    {
        await Page.GotoAsync(BaseUrl);

        await AssertNoHorizontalOverflowAsync();
        await Assertions.Expect(Page.Locator("#sideNavToggle")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("a.brand-logo")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("section.hero")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ProductDetailPage_At375px_NoHorizontalOverflow_AddToCartVisible()
    {
        await OpenFirstProductDetailAsync();

        await AssertNoHorizontalOverflowAsync();
        await Assertions.Expect(Page.Locator("button.pdp-btn-cart")).ToBeVisibleAsync();
    }
}
