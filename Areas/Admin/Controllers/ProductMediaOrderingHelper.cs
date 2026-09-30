using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Shared between ProductPhotosController (an Upload) and ProductsController.Edit (just opening
/// the page) — both need to be able to (re-)derive a color's ProductMedia ordering/IsColorMain
/// and refresh the storefront display fields that read from it (Product.ImageUrl/ProductImages,
/// ProductVariant.ImageUrl), and both need it to be genuinely idempotent: calling it again on
/// already-correct data must be a no-op, and calling it on already-WRONG data (from before this
/// fix existed) must repair it, since "re-upload the same files" and "just open the Edit page"
/// are the two ways this repairs a product on production without SQL.
/// </summary>
internal static class ProductMediaOrderingHelper
{
    // Matches ProductPhotosController.FileNamePattern's "VendorSku-ColorCode[-suffix].ext"
    // filename convention against a value already known to be a real, uploaded blob URL (never
    // this placeholder) — see IsMainMediaUrl.
    internal const string PlaceholderMediaUrl = "TBD - needs hosted URL";

    /// <summary>
    /// True when <paramref name="mediaUrl"/>'s filename has no "-suffix" segment — i.e. it's the
    /// file the vendor (or admin) uploaded as "VendorSku-ColorCode.ext" with nothing after the
    /// color code, which ProductPhotosController's own naming convention treats as that color's
    /// main/front photo. A blob URL's last path segment is exactly the (lowercased) original
    /// filename — see ProductPhotosController.Upload's safeFileName/GetBlobClient call — so
    /// re-parsing it here needs no extra state beyond the URL itself, which is what makes this
    /// safe to re-derive from scratch any number of times instead of trusting a stored flag.
    /// </summary>
    internal static bool IsMainMediaUrl(string mediaUrl)
    {
        var fileName = mediaUrl.Split('/').Last();
        var match = ProductPhotosController.FileNamePattern.Match(fileName);
        return match.Success && !match.Groups["suffix"].Success;
    }

    /// <summary>
    /// Re-derives IsColorMain and a clean, gapless DisplayOrder (main first, then every other
    /// real photo ordered by filename) for one color's ProductMedia rows, from each row's own
    /// MediaUrl alone — never from upload order, a count of "real" rows seen so far, or any
    /// previously-stored IsColorMain/DisplayOrder value. That's deliberate: those were exactly
    /// the inputs the original bug got wrong (see ProductPhotosController.Upload's remarks on
    /// GetMediaForColorAsync), and a fix built the same way would only have been correct going
    /// forward, not self-healing for a product already in the wrong state. Being a pure function
    /// of MediaUrl means calling this again always converges on the same correct result
    /// regardless of how wrong the stored state was — which is what lets a plain re-upload, or
    /// even just opening the Edit page (see ProductsController.Edit), repair it without SQL.
    /// Placeholder rows (never a real photo) are left untouched.
    /// </summary>
    internal static void NormalizeColorMediaOrdering(IEnumerable<ProductMedia> colorMedia)
    {
        var ordered = colorMedia
            .Where(m => m.MediaUrl != PlaceholderMediaUrl)
            .OrderByDescending(m => IsMainMediaUrl(m.MediaUrl))
            .ThenBy(m => m.MediaUrl, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var order = 1;
        foreach (var media in ordered)
        {
            media.IsColorMain = IsMainMediaUrl(media.MediaUrl);
            media.DisplayOrder = order++;
        }
    }

    /// <summary>
    /// Refreshes Product.ImageUrl/ProductImages (the flat legacy gallery every page falls back
    /// to) and each color's ProductVariant.ImageUrl (used by the listing card and cart) from the
    /// current ProductMedia state — moved out of ProductPhotosController (it used to run only
    /// after an Upload) so ProductsController.Edit can call it too, after normalizing ordering,
    /// so "listing cards and cart" actually picks up a fix made by just opening the Edit page,
    /// not only a fresh Upload.
    /// </summary>
    internal static async Task SyncStorefrontDisplayFieldsAsync(ApplicationDbContext context, IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count == 0) return;

        var products = await context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        var media = await context.ProductMedia
            .Where(m => productIds.Contains(m.ProductId) && m.Active && m.MediaType == "Image")
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();
        var productColors = await context.ProductColors.Where(pc => productIds.Contains(pc.ProductId)).ToListAsync();
        var existingImages = await context.ProductImages.Where(pi => productIds.Contains(pi.ProductId)).ToListAsync();
        context.ProductImages.RemoveRange(existingImages);
        var variants = await context.ProductVariants
            .Where(v => productIds.Contains(v.ProductId) && v.ProductColorId != null)
            .ToListAsync();

        foreach (var product in products)
        {
            var productMedia = media.Where(m => m.ProductId == product.Id).ToList();
            if (productMedia.Count == 0) continue;

            var defaultColor = productColors.FirstOrDefault(pc => pc.ProductId == product.Id && pc.DefaultColor)
                ?? productColors.FirstOrDefault(pc => pc.ProductId == product.Id);

            var sharedImages = productMedia.Where(m => m.ProductColorId == null).OrderBy(m => m.DisplayOrder).ToList();
            var defaultColorImages = defaultColor == null
                ? new List<ProductMedia>()
                : productMedia.Where(m => m.ProductColorId == defaultColor.Id).OrderBy(m => m.DisplayOrder).ToList();

            var galleryImages = sharedImages.Concat(defaultColorImages).ToList();
            if (galleryImages.Count > 0)
            {
                product.ImageUrl = galleryImages[0].MediaUrl;

                var sortOrder = 0;
                foreach (var img in galleryImages)
                {
                    context.ProductImages.Add(new ProductImage
                    {
                        ProductId = product.Id,
                        ImageUrl = img.MediaUrl,
                        Label = img.MediaRole,
                        SortOrder = sortOrder++
                    });
                }
            }

            foreach (var colorGroup in productColors.Where(pc => pc.ProductId == product.Id))
            {
                var mainImage = productMedia.FirstOrDefault(m => m.ProductColorId == colorGroup.Id && m.IsColorMain)
                    ?? productMedia.FirstOrDefault(m => m.ProductColorId == colorGroup.Id);
                var imageUrl = mainImage?.MediaUrl ?? colorGroup.SwatchImageUrl;
                if (imageUrl == null) continue;

                foreach (var variant in variants.Where(v => v.ProductColorId == colorGroup.Id))
                {
                    variant.ImageUrl = imageUrl;
                }
            }
        }

        await context.SaveChangesAsync();
    }
}
