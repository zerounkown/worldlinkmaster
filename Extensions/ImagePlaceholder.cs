namespace WorldLinkMaster.Web.Extensions;

public static class ImagePlaceholder
{
    public const string DataUri = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='400' height='400'%3E%3Crect width='100%25' height='100%25' fill='%23e5e0d8'/%3E%3Ctext x='50%25' y='50%25' font-family='sans-serif' font-size='20' fill='%23999' text-anchor='middle' dy='.3em'%3ENo Image%3C/text%3E%3C/svg%3E";

    // Bulk product imports (e.g. the Condor catalog) seed ProductMedia/ProductColor image
    // fields with this literal string instead of leaving them null when no photo was sourced
    // yet. It's a non-empty value, so plain null/empty checks let it through and render as a
    // broken <img src>. Anything comparing an image URL for "is this real" must check both.
    public const string PendingText = "TBD - needs hosted URL";

    public static bool IsRealImageUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) && !url.Contains(PendingText, StringComparison.OrdinalIgnoreCase);
}
