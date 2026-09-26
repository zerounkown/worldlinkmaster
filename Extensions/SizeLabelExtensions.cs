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
}
