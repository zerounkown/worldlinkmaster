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

    // Regression test for a bug where items added via the AJAX cart-drawer flow (PR #142) never
    // made it into /Cart for anonymous visitors. Deliberately does NOT call AuthPages.RegisterAsync
    // -- E2ETestBase hands each test class a fresh IBrowserContext with no auth cookies, so this
    // is already a genuinely cookie-less, anonymous session. The drawer's own AJAX refresh isn't
    // enough evidence the cart persisted -- the assertion has to survive a real top-level
    // navigation to /Cart, which is exactly what was broken.
    [Fact]
    public async Task AnonymousSession_AddToCartViaDrawer_PersistsAcrossFullPageNavigation()
    {
        await OpenFirstProductDetailAsync();
        var pdp = new ProductDetailPage(Page);
        await pdp.AddToCartAsync();
        await Assertions.Expect(Page.Locator("#cartDrawer.open")).ToBeVisibleAsync();

        await Page.GotoAsync(Url("Cart"));
        var cart = new CartPage(Page);
        await Assertions.Expect(cart.QuantityValue).ToHaveTextAsync("1");
    }
}
