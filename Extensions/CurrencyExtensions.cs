using System.Globalization;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Extensions;

public static class CurrencyExtensions
{
    // AED is pegged to USD at a fixed rate; used only for display conversion — all prices
    // are still stored and billed in AED regardless of what a shopper chooses to see.
    private const decimal AedToUsdRate = 3.67m;

    public static string ToAed(this decimal amount)
    {
        return "AED " + amount.ToString("N2", CultureInfo.InvariantCulture);
    }

    // Storefront-facing price display: honors the visitor's chosen currency (AED/USD).
    // Back-office views (Admin/Merchant/Support) deliberately keep using ToAed() above so
    // internal figures stay in one currency regardless of a shopper's cookie.
    public static string ToDisplayCurrency(this decimal amountInAed)
    {
        if (CurrencyContext.Current == "USD")
        {
            var usd = amountInAed / AedToUsdRate;
            return "$" + usd.ToString("N2", CultureInfo.InvariantCulture);
        }

        return amountInAed.ToAed();
    }

    // Reverse of ToDisplayCurrency — used when a shopper types a price filter value in
    // whichever currency they're currently viewing the store in, so it can be compared
    // against the AED values products are actually stored/queried in.
    public static decimal FromDisplayCurrencyToAed(this decimal amountInDisplayCurrency)
    {
        return CurrencyContext.Current == "USD" ? amountInDisplayCurrency * AedToUsdRate : amountInDisplayCurrency;
    }
}
