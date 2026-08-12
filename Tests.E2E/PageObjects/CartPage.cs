using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.PageObjects;

/// <summary>Views/Cart/Index.cshtml. Quantity +/- buttons call Cart/UpdateQuantityAjax under the hood.</summary>
public class CartPage
{
    private readonly IPage _page;

    public CartPage(IPage page)
    {
        _page = page;
    }

    public ILocator QuantityPlusButton => _page.Locator(".cart-qty-plus").First;
    public ILocator QuantityValue => _page.Locator(".cart-qty-value").First;
    public ILocator ProceedToCheckoutLink => _page.Locator("a.btn-wlm-black", new PageLocatorOptions { HasTextString = "CHECKOUT" });

    public async Task IncrementFirstItemQuantityAsync()
    {
        await QuantityPlusButton.ClickAsync();
        // The +/- buttons update the quantity via an AJAX call that re-renders the row, which can
        // detach and replace the ".cart-qty-value" DOM node — Assertions.Expect re-resolves the
        // locator on every poll (unlike a captured ElementHandle, which would go stale).
        await Assertions.Expect(QuantityValue).Not.ToHaveTextAsync("1");
    }
}
