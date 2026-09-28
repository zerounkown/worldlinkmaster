using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;

namespace WorldLinkMaster.E2E.Journeys;

/// <summary>
/// Views/Products/Details.cshtml + Views/Products/_PdpColorGallery.cshtml + wwwroot/js/product-gallery.js.
/// "sentinel-combat-shirt" is used throughout rather than "the first product in the listing" —
/// Data/SeedData.cs's Variants dictionary gives it 3 curated colors plus the 5 shared
/// BonusColors (8 total, including "Khaki"), so it's guaranteed to have a multi-image gallery
/// and the arrow pair is guaranteed visible, unlike an arbitrary first-in-listing product.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class ProductGalleryTests : E2ETestBase
{
    public ProductGalleryTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    private async Task<ProductDetailPage> OpenGalleryProductAsync()
    {
        await Page.GotoAsync(Url("Products/Details?slug=sentinel-combat-shirt"));
        var detail = new ProductDetailPage(Page);
        await detail.MainImage.WaitForAsync();
        return detail;
    }

    [Fact]
    public async Task MainImageArrows_CycleGallery_WrapAround_AndSyncActiveThumbnail()
    {
        var detail = await OpenGalleryProductAsync();

        await Assertions.Expect(detail.MainImageArrowNext).ToBeVisibleAsync();
        await Assertions.Expect(detail.MainImageArrowPrev).ToBeVisibleAsync();

        var thumbCount = await detail.Thumbs.CountAsync();
        Assert.True(thumbCount > 1);

        var firstSrc = await detail.MainImage.GetAttributeAsync("src");

        await detail.MainImageArrowNext.ClickAsync();
        var secondSrc = await detail.MainImage.GetAttributeAsync("src");
        Assert.NotEqual(firstSrc, secondSrc);
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", secondSrc!);

        await detail.MainImageArrowPrev.ClickAsync();
        var backToFirstSrc = await detail.MainImage.GetAttributeAsync("src");
        Assert.Equal(firstSrc, backToFirstSrc);

        // Wrap-around: Prev from the first item goes to the LAST gallery item, not nowhere.
        var lastThumbSrc = await detail.Thumbs.Last.GetAttributeAsync("data-full");
        await detail.MainImageArrowPrev.ClickAsync();
        var wrappedSrc = await detail.MainImage.GetAttributeAsync("src");
        Assert.Equal(lastThumbSrc, wrappedSrc);
        await Assertions.Expect(detail.ActiveThumb).ToHaveAttributeAsync("data-full", lastThumbSrc!);
    }

    [Fact]
    public async Task Lightbox_ArrowsWrapAround_AndKeyboardAndEscapeWork()
    {
        var detail = await OpenGalleryProductAsync();
        await detail.OpenLightboxAsync();

        await Assertions.Expect(detail.LightboxNext).ToBeVisibleAsync();
        await Assertions.Expect(detail.LightboxPrev).ToBeVisibleAsync();
        await Assertions.Expect(detail.LightboxCounter).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^1 / \d+$"));

        var firstSrc = await detail.LightboxImage.GetAttributeAsync("src");

        // Wrap-around the OTHER way: Prev from the first item goes straight to the last one.
        await detail.LightboxPrev.ClickAsync();
        await Assertions.Expect(detail.LightboxImage).Not.ToHaveAttributeAsync("src", firstSrc!);
        var lastSrc = await detail.LightboxImage.GetAttributeAsync("src");

        // Keyboard ArrowRight navigates forward, back to the first item (wrapping again).
        await Page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(detail.LightboxImage).ToHaveAttributeAsync("src", firstSrc!);

        await Page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(detail.LightboxImage).ToHaveAttributeAsync("src", lastSrc!);

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(detail.Lightbox).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("open"));
    }

    [Fact]
    public async Task ColorLabel_ShowsSelectedColorName_AndUpdatesLiveOnSelection()
    {
        var detail = await OpenGalleryProductAsync();

        var swatchCount = await detail.ColorSwatches.CountAsync();
        Assert.True(swatchCount > 1);

        var initialLabel = await detail.SelectedColorLabel.InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(initialLabel));

        // The label sits inside "COLOR: <name>" — assert the surrounding text carries the
        // localized "Color" heading alongside the live value, not just the bare name.
        // Case-insensitive: .option-label is rendered all-uppercase via CSS text-transform
        // (InnerTextAsync reflects that, unlike the DOM's own textContent), so the heading
        // itself reads "COLOR" on the page even though the Razor source says "Color".
        var optionLabelText = await Page.Locator("label.option-label", new PageLocatorOptions { Has = detail.SelectedColorLabel }).InnerTextAsync();
        Assert.Contains("Color", optionLabelText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(initialLabel, optionLabelText);

        // Pick a swatch that isn't already selected and confirm the label updates to match.
        var otherSwatch = detail.ColorSwatches.Locator("input:not(:checked)").First;
        var otherColorName = await otherSwatch.GetAttributeAsync("data-label");
        await otherSwatch.ClickAsync(new LocatorClickOptions { Force = true });

        await Assertions.Expect(detail.SelectedColorLabel).ToHaveTextAsync(otherColorName!);
        Assert.NotEqual(initialLabel, otherColorName);
    }
}
