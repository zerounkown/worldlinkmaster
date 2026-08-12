using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class CheckoutCompletionTests : E2ETestBase
{
    public CheckoutCompletionTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task FullCheckout_WithStripeTestCard_ReachesConfirmation()
    {
        var (email, password) = AuthPages.GenerateCredentials();
        var auth = new AuthPages(Page, BaseUrl);
        await auth.RegisterAsync(email, password);

        await OpenFirstProductDetailAsync();
        var pdp = new ProductDetailPage(Page);
        await pdp.AddToCartAsync();

        await Page.GotoAsync(Url("Checkout"));

        var wizard = new CheckoutWizard(Page);
        await wizard.FillShippingAsync();
        await wizard.VerifyOtpAsync();
        await wizard.FillStripeTestCardAsync();
        await wizard.PlaceOrderAsync();

        Assert.Contains("/Checkout/Confirmation/", Page.Url);
    }
}
