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

        // Stripe's Payment Element infers a billing country from the environment (confirmed via
        // form-field dumps: this sandbox resolves "AE", the GitHub-hosted CI runner resolves "US"),
        // which changes what postal-code/phone-number formats it will accept and cascades into
        // Link's own country selector too. Forcing it to a fixed "US" makes every downstream field
        // format deterministic regardless of which environment this runs in, instead of chasing a
        // different valid format per country.
        var countrySelect = stripeFrame.Locator("select[name='country']");
        if (await countrySelect.CountAsync() > 0)
        {
            await countrySelect.SelectOptionAsync("US");
        }

        // Field names below (postalCode, linkMobilePhoneCountry, linkMobilePhone, linkLegalName)
        // confirmed via real form-field dumps — Stripe's "autocomplete" attributes on this form are
        // compound (e.g. "billing postal-code") and its field names get progressively revealed as
        // Link enrollment fields are filled in, so each is guarded with a CountAsync() check rather
        // than assumed present.
        var postal = stripeFrame.Locator("input[name='postalCode']");
        if (await postal.CountAsync() > 0)
        {
            await postal.FillAsync("10001");
        }

        // Stripe's Link enrollment section (shown once billing email is known — see
        // checkout-wizard.js's defaultValues.billingDetails.email) requires a mobile number before
        // confirmPayment() will succeed, even though this app never uses Link itself.
        var linkPhoneCountry = stripeFrame.Locator("select[name='linkMobilePhoneCountry']");
        if (await linkPhoneCountry.CountAsync() > 0)
        {
            await linkPhoneCountry.SelectOptionAsync("US");
        }

        var linkPhone = stripeFrame.Locator("input[name='linkMobilePhone']");
        if (await linkPhone.CountAsync() > 0)
        {
            await linkPhone.FillAsync("2015551234");
        }

        var linkLegalName = stripeFrame.Locator("input[name='linkLegalName']");
        if (await linkLegalName.CountAsync() > 0)
        {
            await linkLegalName.FillAsync("Jordan E2E");
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

            // "Your ZIP is invalid" surfaced here in CI even after changing the postal value —
            // dump the actual rendered Stripe form (every input/select, its name/autocomplete,
            // and current value) rather than guess again, since the payment panel is hidden
            // (step 3, review, is showing) by the time this runs but the iframe/fields still exist.
            var stripeFormDump = "(could not inspect)";
            try
            {
                var stripeFrame = _page.FrameLocator("#stripe-payment-element iframe[title='Secure payment input frame']");
                stripeFormDump = await stripeFrame.Locator("form, div").First.EvaluateAsync<string>(
                    """
                    el => {
                      const root = el.closest('form') || el.ownerDocument.body;
                      const fields = [...root.querySelectorAll('input, select')];
                      return fields.map(f => `${f.tagName}[name=${f.name}][autocomplete=${f.autocomplete}]=${f.value}`).join(' | ');
                    }
                    """);
            }
            catch
            {
                // Best-effort diagnostic only — don't let a failure here mask the real exception.
            }

            throw new InvalidOperationException(
                $"Checkout never reached the Confirmation page. Current URL: {_page.Url}. #reviewError text: '{errorText}'. " +
                $"JS page errors: [{string.Join(" | ", jsErrors)}]. Stripe form fields at failure time: {stripeFormDump}");
        }
    }
}
