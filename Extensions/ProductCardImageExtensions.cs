using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Extensions;

// Powers the product-card hover crossfade (listing/category grids, related products, featured
// products) — resolves a second, genuinely different product image so hovering swaps to a real
// detail shot instead of just zooming. Mirrors the shared-media + default-color gallery logic
// already used by Products/Details.cshtml, just narrowed to "first distinct image".
public static class ProductCardImageExtensions
{
    public static string? GetHoverImageUrl(this Product product, string? primaryImageUrl)
    {
        var defaultColor = product.ProductColors.FirstOrDefault(pc => pc.Active && pc.DefaultColor)
            ?? product.ProductColors.FirstOrDefault(pc => pc.Active);

        var sharedImages = product.Media
            .Where(m => m.Active && m.ShowInGallery && m.MediaType == "Image" && m.ProductColorId == null)
            .OrderBy(m => m.DisplayOrder)
            .Select(m => m.MediaUrl);

        var colorImages = defaultColor == null
            ? Enumerable.Empty<string>()
            : product.Media
                .Where(m => m.Active && m.ShowInGallery && m.MediaType == "Image" && m.ProductColorId == defaultColor.Id)
                .OrderBy(m => m.DisplayOrder)
                .Select(m => m.MediaUrl);

        return sharedImages.Concat(colorImages)
            .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url) && !string.Equals(url, primaryImageUrl, StringComparison.OrdinalIgnoreCase));
    }
}
