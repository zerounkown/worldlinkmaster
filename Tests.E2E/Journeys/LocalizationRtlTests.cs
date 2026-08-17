using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class LocalizationRtlTests : E2ETestBase
{
    public LocalizationRtlTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private async Task SwitchToArabicAsync()
    {
        // Views/Shared/_Layout.cshtml — the EN | AR toggle is two plain-text buttons, each its
        // own real POST form to Localization/SetLanguage, submitted directly with no dropdown
        // panel to open first.
        await Page.ClickAsync("form:has(input[name='culture'][value='ar']) button[type='submit']");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Fact]
    public async Task SwitchToArabic_RendersRtl_OnHomepageAndProductPage()
    {
        await Page.GotoAsync(BaseUrl);
        await SwitchToArabicAsync();

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "ar");
        await Assertions.Expect(Page.Locator("link[href*='bootstrap.rtl.min.css']")).ToHaveCountAsync(1);

        await OpenFirstProductDetailAsync();

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("dir", "rtl");
        await Assertions.Expect(Page.Locator("button.pdp-btn-cart")).ToBeVisibleAsync();
    }
}
