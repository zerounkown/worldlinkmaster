using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.PageObjects;

/// <summary>
/// Views/Products/Details.cshtml. Color/size (when present) default to a pre-checked radio, so
/// "add to cart" needs no explicit variant selection. Add-to-cart is a real full-page POST to
/// Cart/Add (not AJAX). The inline review form (Views/Products/Details.cshtml ~L584-603) only
/// renders for authenticated users.
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

    public async Task AddToCartAsync()
    {
        await AddToCartButton.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task OpenReviewsTabAsync()
    {
        // The review form lives inside a Bootstrap pill tab-pane (#pdpTabReviews) that starts
        // hidden — the "Description" tab is active by default.
        await _page.ClickAsync("[data-bs-target='#pdpTabReviews']");
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
