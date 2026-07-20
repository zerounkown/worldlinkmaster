using Microsoft.AspNetCore.Mvc;

namespace WorldLinkMaster.Web.Controllers;

public class CurrencyController : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SetCurrency(string currency, string returnUrl)
    {
        var normalized = currency == "USD" ? "USD" : "AED";
        Response.Cookies.Append(
            "currency",
            normalized,
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }
}
