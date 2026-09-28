using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.PageObjects;

/// <summary>
/// Views/Products/Details.cshtml. Color/size (when present) default to a pre-checked radio, so
/// "add to cart" needs no explicit variant selection. Add-to-cart is an AJAX call intercepted by
/// wwwroot/js/cart-drawer.js (no page navigation) that opens the side cart drawer on success. The
/// inline review form (Views/Products/Details.cshtml ~L584-603) only renders for authenticated
/// users.
/// </summary>
public class ProductDetailPage
{
    private readonly IPage _page;

    public ProductDetailPage(IPage page)
    {
        _page = page;
    }

    public ILocator AddToCartButton => _page.Locator("button.pdp-btn-cart");
    public ILocator QuantityInput => _page.Locator("#cartQuantityInput");
    public ILocator QuantityIncrementButton => _page.Locator("#cartQtyIncrement");
    public ILocator ReviewForm => _page.Locator("form.pdp-review-form");
    public ILocator ReviewCommentInput => _page.Locator("textarea[name='comment']");
    public ILocator ReviewSubmitButton => _page.Locator("button.pdp-review-submit");

    public ILocator StarButton(int stars) => _page.Locator($"#reviewStarInput .pdp-star-input-btn[data-value='{stars}']");

    // Gallery: main image + its own prev/next arrows, the lightbox opened by clicking it (with
    // its own prev/next pair, wrap-around, counter), the thumbnail strip, the color swatches,
    // and the "COLOR: <name>" label that tracks whichever color is currently selected.
    public ILocator MainImage => _page.Locator("#mainProductImage");
    public ILocator MainImageArrowPrev => _page.Locator("#mainImageArrowPrev");
    public ILocator MainImageArrowNext => _page.Locator("#mainImageArrowNext");
    public ILocator ActiveThumb => _page.Locator(".product-thumb.active");
    public ILocator Thumbs => _page.Locator(".product-thumb");
    public ILocator ColorSwatches => _page.Locator(".color-swatch");
    public ILocator SelectedColorLabel => _page.Locator("#selectedColorLabel");
    public ILocator Lightbox => _page.Locator("#zoomLightbox");
    public ILocator LightboxImage => _page.Locator("#zoomLightboxImage");
    public ILocator LightboxPrev => _page.Locator("#zoomLightboxPrev");
    public ILocator LightboxNext => _page.Locator("#zoomLightboxNext");
    public ILocator LightboxCounter => _page.Locator("#zoomLightboxCounter");

    public async Task OpenLightboxAsync()
    {
        await MainImage.ClickAsync();
        await Assertions.Expect(Lightbox).ToHaveClassAsync(new Regex("open"));
    }

    public async Task AddToCartAsync()
    {
        await AddToCartButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task OpenReviewsTabAsync()
    {
        // The review form lives inside a Bootstrap collapse accordion panel (#pdpAccReviews)
        // that starts closed. Guarded on aria-expanded rather than always clicking — unlike the
        // Bootstrap Tab system this replaced, a collapse trigger toggles closed if clicked while
        // already open, and SubmitReviewAsync below calls this a second time after the caller
        // may have already opened it once.
        var trigger = _page.Locator("[data-bs-target='#pdpAccReviews']");
        if (await trigger.GetAttributeAsync("aria-expanded") != "true")
        {
            await trigger.ClickAsync();
        }
        await ReviewForm.WaitForAsync();
    }

    public async Task SubmitReviewAsync(int stars, string comment)
    {
        await OpenReviewsTabAsync();
        await StarButton(stars).ClickAsync();
        await ReviewCommentInput.FillAsync(comment);
        await ReviewSubmitButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
