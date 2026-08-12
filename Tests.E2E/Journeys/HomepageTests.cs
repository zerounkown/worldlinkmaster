using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class HomepageTests : E2ETestBase
{
    public HomepageTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task Homepage_Loads_HeroAndCategorySectionsRender()
    {
        var response = await Page.GotoAsync(BaseUrl);

        Assert.NotNull(response);
        Assert.True(response!.Ok);

        // Views/Home/Welcome.cshtml — the ".hero" section always renders; the home banner
        // carousel is conditional on seeded HomeBanners rows, which SeedData does not create.
        await Assertions.Expect(Page.Locator("section.hero")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("section.hero h1")).ToBeVisibleAsync();

        // "Shop by Category" grid — conditional on seeded categories, which SeedData does create.
        await Assertions.Expect(Page.Locator(".collections-grid")).ToBeVisibleAsync();
        var categoryTiles = Page.Locator(".collections-tile");
        Assert.True(await categoryTiles.CountAsync() > 0);
    }
}
