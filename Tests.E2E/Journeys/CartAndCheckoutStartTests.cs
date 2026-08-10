using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class CartAndCheckoutStartTests : E2ETestBase
{
    public CartAndCheckoutStartTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task AddToCart_AdjustQuantity_ReachCheckout()
    {
        var (email, password) = AuthPages.GenerateCredentials();
        var auth = new AuthPages(Page, BaseUrl);
        await auth.RegisterAsync(email, password);
        await Assertions.Expect(auth.LogoutForm).ToBeAttachedAsync();

        await OpenFirstProductDetailAsync();
        var pdp = new ProductDetailPage(Page);
        await pdp.AddToCartAsync();

        await Page.GotoAsync(Url("Cart"));
        var cart = new CartPage(Page);
        await Assertions.Expect(cart.QuantityValue).ToHaveTextAsync("1");
        await cart.IncrementFirstItemQuantityAsync();
        await Assertions.Expect(cart.QuantityValue).ToHaveTextAsync("2");

        await cart.ProceedToCheckoutLink.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/Checkout"));
        await Assertions.Expect(Page.Locator("#checkout-wizard-root")).ToBeVisibleAsync();
    }
}
