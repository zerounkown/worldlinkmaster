using System.Text.RegularExpressions;

namespace WorldLinkMaster.Web.Extensions;

public static class SizeLabelExtensions
{
    // Reformats a "28x30"-style Waist × Length Size.Label for display as "28 / 30" — used
    // anywhere a raw Size.Label reaches the shopper (product card, cart, order) so the combined
    // value reads the same way the split Waist/Length picker on the product page presents it.
    // Deliberately tolerant of the separator character (matches "any run of non-digit characters
    // between two numbers", not a specific "x" or "×" — see Views/Products/Details.cshtml for
    // why) and a no-op for every other size shape (S/M/L, "4R", plain numbers, ...), which is
    // returned completely unchanged.
    private static readonly Regex WaistLengthPattern = new(@"^(\d+)\D+(\d+)$");

    public static string ToDisplaySizeLabel(this string? label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return label ?? string.Empty;
        }

        var match = WaistLengthPattern.Match(label);
        return match.Success ? $"{match.Groups[1].Value} / {match.Groups[2].Value}" : label;
    }

    // Same pattern as above, but split into its two parts instead of reformatted into one string
    // — used wherever a UI wants to label Waist and Length as their own distinct fields (e.g. the
    // cart drawer) rather than a single combined "Size" value. Returns false for every other size
    // shape, in which case the caller falls back to showing the raw label as a plain "Size".
    public static bool TryGetWaistLength(this string? label, out string waist, out string length)
    {
        waist = string.Empty;
        length = string.Empty;
        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        var match = WaistLengthPattern.Match(label);
        if (!match.Success)
        {
            return false;
        }

        waist = match.Groups[1].Value;
        length = match.Groups[2].Value;
        return true;
    }
}
