using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class ProductReviewTests : E2ETestBase
{
    public ProductReviewTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task SubmitReview_WhileLoggedIn_ShowsUpAfterSubmit()
    {
        var (email, password) = AuthPages.GenerateCredentials();
        var auth = new AuthPages(Page, BaseUrl);
        await auth.RegisterAsync(email, password);

        await OpenFirstProductDetailAsync();
        var pdp = new ProductDetailPage(Page);
        await pdp.OpenReviewsTabAsync();
        await Assertions.Expect(pdp.ReviewForm).ToBeVisibleAsync();

        const string comment = "Solid build quality, worked exactly as described in the field.";
        await pdp.SubmitReviewAsync(stars: 5, comment: comment);

        // Controllers/ReviewsController.Submit redirects back to Products/Details?slug=... with
        // TempData["ReviewMessage"] set, and the form switches to "Update Your Review" heading
        // once a review from this user already exists for the product. The submitted comment now
        // appears twice — once read-only in the reviews list (".pdp-review-comment"), once as the
        // still-editable textarea's value — scope the assertion to the list entry specifically.
        await Assertions.Expect(Page.Locator(".pdp-review-comment", new PageLocatorOptions { HasTextString = comment })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".pdp-review-form-heading")).ToContainTextAsync("Update");
    }
}
