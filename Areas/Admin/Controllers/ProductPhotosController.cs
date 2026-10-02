using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Bulk photo upload for vendor products. Filenames are expected in the vendor's own
/// "{VendorSku}-{VendorColorCode}[-N].ext" convention (e.g. Condor's "101228-002.webp" as the
/// main photo, "101228-002-2.webp"/"101228-002-3.webp" as extra gallery angles for that same
/// color, N = MinExtraPhotoNumber..MaxExtraPhotoNumber), or Propper's alphanumeric-style
/// "F5259-BLK.webp" now that real manufacturer SKUs are the standard Product Code — the pattern
/// below accepts letters as well as digits in both the SKU and color-code segments for that
/// reason, not just Condor/Rothco's purely-numeric style. The "-N" suffix itself is numeric-only
/// (never an arbitrary word) so a name like "101119-029-zip-pocket.jpg" is rejected rather than
/// silently added to the gallery — see FileNamePattern's own remarks.
/// Each file is matched to a Product by Product.VendorSku and a ProductColor by
/// (ProductId, VendorColorCode) — both populated by the Product/Master Data importers or a
/// one-off backfill, not by this tool. Files that don't match are reported, not guessed at.
///
/// Uploaded to Azure Blob Storage rather than wwwroot: this App Service runs with
/// WEBSITE_RUN_FROM_PACKAGE=1, which mounts /home/site/wwwroot read-only at runtime, so local
/// disk writes there fail in production even though they work in local dev.
/// </summary>
public class ProductPhotosController : AdminBaseController
{
    private const long MaxFileSizeBytes = 15 * 1024 * 1024; // 15 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp", ".jpg", ".jpeg", ".png"
    };
    // internal, and "suffix"/"n" are named capturing groups (not the original non-capturing
    // one): so ProductMediaOrderingHelper.IsMainMediaUrl/GetExtraPhotoNumber can re-derive "is
    // this the color's main photo" (no suffix) and "which extra photo is this" straight from an
    // already-stored MediaUrl, using the exact same rule this pattern enforces at upload time,
    // rather than a second, potentially-drifting copy of it.
    //
    // The suffix is deliberately numeric-only ("-N"), not the old "any alphanumeric" shape — a
    // name like "101119-029-zip-pocket.jpg" (an extra word, not a photo number) must never be
    // silently accepted as a gallery photo. MinExtraPhotoNumber/MaxExtraPhotoNumber below enforce
    // the 2-20 range the regex's "{1,2} digits" alone can't express.
    internal static readonly Regex FileNamePattern = new(
        @"^(?<sku>[A-Za-z0-9]{2,20})-(?<color>[A-Za-z0-9]{2,10})(?<suffix>-(?<n>[0-9]{1,2}))?\.(?<ext>webp|jpe?g|png)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal const int MinExtraPhotoNumber = 2;
    internal const int MaxExtraPhotoNumber = 20;

    internal readonly record struct ParsedPhotoFileName(string VendorSku, string VendorColorCode, int? ExtraPhotoNumber, string Extension);

    /// <summary>
    /// Pure filename parser — no I/O, no database, no blob storage — so it's unit-testable
    /// without standing up Azure Blob Storage. Upload() is the only caller; kept as its own
    /// method specifically so the "main vs extra vs invalid name" decision has a single,
    /// directly-testable home instead of being buried in Upload()'s per-file loop.
    /// </summary>
    internal static bool TryParsePhotoFileName(string fileName, out ParsedPhotoFileName parsed, out string? error)
    {
        parsed = default;
        error = null;

        var match = FileNamePattern.Match(fileName);
        if (!match.Success)
        {
            error = $"doesn't match the \"VendorSku-ColorCode.ext\" (main photo) or \"VendorSku-ColorCode-N.ext\" (extra photo, N = {MinExtraPhotoNumber}-{MaxExtraPhotoNumber}) naming pattern.";
            return false;
        }

        int? extraPhotoNumber = null;
        if (match.Groups["n"].Success)
        {
            var n = int.Parse(match.Groups["n"].Value);
            if (n < MinExtraPhotoNumber || n > MaxExtraPhotoNumber)
            {
                error = $"the \"-{n}\" suffix must be a number from {MinExtraPhotoNumber} to {MaxExtraPhotoNumber} for an extra gallery photo — the main photo has no suffix at all.";
                return false;
            }
            extraPhotoNumber = n;
        }

        parsed = new ParsedPhotoFileName(
            match.Groups["sku"].Value,
            match.Groups["color"].Value,
            extraPhotoNumber,
            "." + match.Groups["ext"].Value.ToLowerInvariant());
        return true;
    }

    private const string ContainerName = "product-photos";

    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IStorefrontCacheService _storefrontCache;

    public ProductPhotosController(ApplicationDbContext context, IConfiguration configuration, IStorefrontCacheService storefrontCache)
    {
        _context = context;
        _configuration = configuration;
        _storefrontCache = storefrontCache;
    }

    public IActionResult Index()
    {
        return View(new ProductPhotoUploadResult());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(500_000_000)]
    public async Task<IActionResult> Upload(List<IFormFile>? files)
    {
        var result = new ProductPhotoUploadResult();

        if (files == null || files.Count == 0)
        {
            result.Invalid.Add("No files were uploaded.");
            return View("Index", result);
        }

        var connectionString = _configuration["ProductPhotos:BlobConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            result.Invalid.Add("Blob storage isn't configured (ProductPhotos:BlobConnectionString is missing).");
            return View("Index", result);
        }
        var containerClient = new BlobContainerClient(connectionString, ContainerName);
        await containerClient.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.Blob);

        var touchedProductIds = new HashSet<int>();

        // Cache of (product, color) -> its ProductMedia rows, populated lazily from the database
        // and then kept in sync in-memory for the rest of this request — SaveChangesAsync only
        // runs once, after the whole loop, so a plain per-file DB query here would be stale for
        // every file after the first one in the SAME (product, color): it would never see media
        // this SAME upload batch already added a moment ago, making every file in a color look
        // like "the first ever upload" for it. That was the actual bug — see the remarks below.
        var mediaByProductColor = new Dictionary<int, List<ProductMedia>>();

        async Task<List<ProductMedia>> GetMediaForColorAsync(int productId, int productColorId)
        {
            if (!mediaByProductColor.TryGetValue(productColorId, out var media))
            {
                media = await _context.ProductMedia
                    .Where(m => m.ProductId == productId && m.ProductColorId == productColorId)
                    .ToListAsync();
                mediaByProductColor[productColorId] = media;
            }
            return media;
        }

        foreach (var file in files)
        {
            if (file.Length == 0)
            {
                result.Invalid.Add($"{file.FileName}: empty file.");
                continue;
            }
            if (file.Length > MaxFileSizeBytes)
            {
                result.Invalid.Add($"{file.FileName}: too large (15 MB max).");
                continue;
            }

            if (!TryParsePhotoFileName(file.FileName, out var parsed, out var parseError))
            {
                result.Invalid.Add($"{file.FileName}: {parseError}");
                continue;
            }

            if (!AllowedExtensions.Contains(parsed.Extension))
            {
                result.Invalid.Add($"{file.FileName}: unsupported file type.");
                continue;
            }

            var vendorSku = parsed.VendorSku;
            var vendorColorCode = parsed.VendorColorCode;

            var product = await _context.Products.FirstOrDefaultAsync(p => p.VendorSku == vendorSku);
            if (product == null)
            {
                result.UnmatchedProduct.Add(new ProductPhotoUploadResult.UnmatchedPhoto(
                    file.FileName, vendorSku, vendorColorCode, null, null));
                continue;
            }

            var productColors = await _context.ProductColors
                .Include(pc => pc.Color)
                .Where(pc => pc.ProductId == product.Id)
                .ToListAsync();
            var productColor = productColors.FirstOrDefault(pc => pc.VendorColorCode == vendorColorCode);
            if (productColor == null)
            {
                var knownColors = string.Join(", ", productColors.Select(pc => $"{pc.VendorColorCode ?? "?"}={pc.Color?.Name}"));
                result.UnmatchedColor.Add(new ProductPhotoUploadResult.UnmatchedPhoto(
                    file.FileName, vendorSku, vendorColorCode, product.Name, knownColors));
                continue;
            }

            var safeFileName = file.FileName.ToLowerInvariant();
            var blobClient = containerClient.GetBlobClient(safeFileName);
            var contentType = parsed.Extension switch
            {
                ".webp" => "image/webp",
                ".png" => "image/png",
                _ => "image/jpeg"
            };
            await using (var stream = file.OpenReadStream())
            {
                await blobClient.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = contentType },
                    Conditions = null
                });
            }
            var mediaUrl = blobClient.Uri.ToString();

            var existingMedia = await GetMediaForColorAsync(product.Id, productColor.Id);

            var alreadyPresent = existingMedia.Any(m => m.MediaUrl == mediaUrl);
            var placeholder = existingMedia.FirstOrDefault(m => m.MediaUrl == ProductMediaOrderingHelper.PlaceholderMediaUrl);

            var replaced = false;
            if (alreadyPresent)
            {
                // Re-upload of the same file — the URL (and therefore IsColorMain/DisplayOrder,
                // both re-derived from it below) is already correct; nothing to do here. This is
                // also how re-uploading the exact same files repairs a color whose OTHER rows are
                // still wrong from before this fix — NormalizeColorMediaOrdering, below, runs
                // over every row this color has, not just this one.
            }
            else if (placeholder != null)
            {
                placeholder.MediaUrl = mediaUrl;
                replaced = true;
            }
            else
            {
                var newMedia = new ProductMedia
                {
                    ProductId = product.Id,
                    ProductColorId = productColor.Id,
                    MediaScope = "Color",
                    MediaType = "Image",
                    MediaUrl = mediaUrl,
                    // IsColorMain/DisplayOrder are placeholders here — NormalizeColorMediaOrdering
                    // (below, once per color, after every file in this batch is matched)
                    // overwrites both from the final MediaUrl alone.
                    ShowInGallery = true,
                    Active = true
                };
                _context.ProductMedia.Add(newMedia);
                // Keep the cache in sync for the REST of this request — the whole reason
                // existingMedia is cached rather than re-queried per file (see
                // GetMediaForColorAsync's remarks): a later file for this same color, still in
                // this same request, must see this row even though nothing's saved yet.
                existingMedia.Add(newMedia);
            }

            touchedProductIds.Add(product.Id);
            result.Matched.Add(new ProductPhotoUploadResult.MatchedPhoto(
                file.FileName, product.Name, product.Sku, productColor.Color?.Name ?? "", mediaUrl, replaced));
        }

        foreach (var colorMedia in mediaByProductColor.Values)
        {
            ProductMediaOrderingHelper.NormalizeColorMediaOrdering(colorMedia);
        }

        await _context.SaveChangesAsync();
        await ProductMediaOrderingHelper.SyncStorefrontDisplayFieldsAsync(_context, touchedProductIds);
        _storefrontCache.Invalidate();

        return View("Index", result);
    }

    /// <summary>
    /// Deletes one extra (non-main) gallery photo — the Delete button on Admin -> Products ->
    /// Edit's "Color Photos" section. Refuses to delete a color's main photo even if asked to
    /// (that button is never rendered for one, but a stale page or a direct POST shouldn't be
    /// able to leave a color with no main image at all) — re-upload over the main photo's own
    /// filename via Product Photos instead. The blob delete is best-effort: an orphaned blob in
    /// storage is a minor cleanup issue, not worth failing the whole request over, so a failure
    /// there is swallowed rather than blocking the ProductMedia row from being removed.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteExtraPhoto(int mediaId, int productId)
    {
        var media = await _context.ProductMedia.FirstOrDefaultAsync(m => m.Id == mediaId && m.ProductId == productId);
        if (media == null || media.IsColorMain)
        {
            return RedirectToAction("Edit", "Products", new { area = "Admin", id = productId });
        }

        var colorId = media.ProductColorId;
        var mediaUrl = media.MediaUrl;
        _context.ProductMedia.Remove(media);
        await _context.SaveChangesAsync();

        try
        {
            var connectionString = _configuration["ProductPhotos:BlobConnectionString"];
            if (!string.IsNullOrEmpty(connectionString))
            {
                var containerClient = new BlobContainerClient(connectionString, ContainerName);
                var fileName = mediaUrl.Split('/').Last();
                await containerClient.GetBlobClient(fileName).DeleteIfExistsAsync();
            }
        }
        catch
        {
            // Best-effort — see the method's remarks.
        }

        if (colorId.HasValue)
        {
            var remainingColorMedia = await _context.ProductMedia
                .Where(m => m.ProductId == productId && m.ProductColorId == colorId.Value)
                .ToListAsync();
            ProductMediaOrderingHelper.NormalizeColorMediaOrdering(remainingColorMedia);
            await _context.SaveChangesAsync();
        }

        await ProductMediaOrderingHelper.SyncStorefrontDisplayFieldsAsync(_context, new[] { productId });
        _storefrontCache.Invalidate();

        return RedirectToAction("Edit", "Products", new { area = "Admin", id = productId });
    }
}
