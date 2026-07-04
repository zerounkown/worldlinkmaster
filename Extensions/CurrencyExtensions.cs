using System.Globalization;

namespace WorldLinkMaster.Web.Extensions;

public static class CurrencyExtensions
{
    public static string ToAed(this decimal amount)
    {
        return "AED " + amount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
