using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.PageObjects;

/// <summary>
/// Views/Checkout/Index.cshtml + wwwroot/js/checkout-wizard.js. Shipping -> OTP modal -> Stripe
/// Payment Element (iframe) -> review -> place order. In Development, the OTP AJAX response
/// echoes back "devOtpCode" and the wizard's own JS auto-fills #otpCodeInput with it, so no real
/// email inbox is needed here — just click Verify.
/// </summary>
public class CheckoutWizard
{
    private readonly IPage _page;

    public CheckoutWizard(IPage page)
    {
        _page = page;
    }

    public async Task FillShippingAsync(
        string name = "Jordan E2E",
        string address = "1 Test Street",
        string city = "Dubai",
        string state = "Dubai",
        string zip = "00000",
        string phone = "0500000000")
    {
        await _page.FillAsync("#ShippingName", name);
        await _page.FillAsync("#ShippingAddress", address);
        await _page.FillAsync("#ShippingCity", city);
        await _page.FillAsync("#ShippingState", state);
        await _page.FillAsync("#ShippingZip", zip);
        await _page.FillAsync("#ShippingPhone", phone);
        await _page.ClickAsync("#shippingContinueBtn");
    }

    public async Task VerifyOtpAsync()
    {
        var otpInput = _page.Locator("#otpCodeInput");
        // The wizard's own JS pre-fills this with the dev-mode OTP as soon as the modal opens.
        await otpInput.WaitForAsync();
        await Assertions.Expect(otpInput).Not.ToHaveValueAsync(string.Empty);
        await _page.ClickAsync("#otpVerifyBtn");
        await _page.Locator("#checkoutPanelPayment:not(.d-none)").WaitForAsync();
    }

    public async Task FillStripeTestCardAsync()
    {
        if (await _page.Locator("#paymentError.visible").CountAsync() > 0)
        {
            var errorText = await _page.Locator("#paymentError").InnerTextAsync();
            throw new InvalidOperationException($"Payment Element failed to initialize — #paymentError says: '{errorText}'.");
        }

        // Stripe's Payment Element mounts its actual input fields into the iframe titled "Secure
        // payment input frame" — its src is misleadingly named "elements-inner-accessory-target",
        // not "...-payment", but it is the real input frame (confirmed by inspecting the live DOM).
        var stripeFrame = _page.FrameLocator("#stripe-payment-element iframe[title='Secure payment input frame']");

        var cardNumberField = stripeFrame.Locator("input[autocomplete='cc-number']");
        try
        {
            await cardNumberField.WaitForAsync(new LocatorWaitForOptions { Timeout = 20000 });
        }
        catch (TimeoutException)
        {
            var mountHtml = await _page.Locator("#stripe-payment-element").InnerHTMLAsync();
            throw new InvalidOperationException(
                $"Stripe card number input (input[autocomplete='cc-number']) not found within 20s. Mount point HTML: {mountHtml}");
        }

        await cardNumberField.FillAsync("4242424242424242");
        await stripeFrame.Locator("input[autocomplete='cc-exp']").FillAsync("12/34");
        await stripeFrame.Locator("input[autocomplete='cc-csc']").FillAsync("123");
        var postal = stripeFrame.Locator("input[autocomplete='postal-code']");
        if (await postal.CountAsync() > 0)
        {
            // "00000" passed locally but Stripe's postal-code validation rejected it in CI
            // ("Your ZIP is invalid.") — likely country-dependent validation (Stripe infers the
            // billing country from browser/IP signals, which differ between environments) treating
            // an all-zero ZIP as a known-fake placeholder. A real, unambiguously valid US ZIP
            // sidesteps the ambiguity regardless of which country's validation applies.
            await postal.FillAsync("10001");
        }

        await _page.ClickAsync("#paymentContinueBtn");
        await _page.Locator("#checkoutPanelReview:not(.d-none)").WaitForAsync();
    }

    public async Task PlaceOrderAsync()
    {
        // Captured so a failed confirmPayment() (a real call to Stripe's live API) reports the
        // actual client-side error instead of just "the URL never changed" — this is exactly how
        // the missing confirmParams.payment_method_data.billing_details.email bug (fixed in
        // checkout-wizard.js) was diagnosed: the JS threw a Stripe IntegrationError that only
        // surfaces here, not in any server-side log.
        var jsErrors = new List<string>();
        _page.PageError += (_, msg) => jsErrors.Add(msg);

        await _page.ClickAsync("#placeOrderBtn");
        try
        {
            await _page.WaitForURLAsync(url => url.Contains("/Checkout/Confirmation/"), new PageWaitForURLOptions { Timeout = 25000 });
        }
        catch (TimeoutException)
        {
            var errorText = await _page.Locator("#reviewError").InnerTextAsync();
            throw new InvalidOperationException(
                $"Checkout never reached the Confirmation page. Current URL: {_page.Url}. #reviewError text: '{errorText}'. " +
                $"JS page errors: [{string.Join(" | ", jsErrors)}].");
        }
    }
}
