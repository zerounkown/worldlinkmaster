using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Bulk photo upload for vendor products. Filenames are expected in the vendor's own
/// "{VendorSku}-{VendorColorCode}[-suffix].ext" convention (e.g. Condor's "101228-002.webp",
/// or Propper's alphanumeric-style "F5259-BLK.webp" now that real manufacturer SKUs are the
/// standard Product Code — the pattern below accepts letters as well as digits in both the SKU
/// and color-code segments for that reason, not just Condor/Rothco's purely-numeric style.
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
    // internal, and "suffix" is a named capturing group (not the original non-capturing one): so
    // ProductMediaOrderingHelper.IsMainMediaUrl can re-derive "is this the color's main photo"
    // (no suffix) straight from an already-stored MediaUrl, using the exact same rule this
    // pattern enforces at upload time, rather than a second, potentially-drifting copy of it.
    internal static readonly Regex FileNamePattern = new(
        @"^(?<sku>[A-Za-z0-9]{2,20})-(?<color>[A-Za-z0-9]{2,10})(?<suffix>-[A-Za-z0-9]+)?\.(?<ext>webp|jpe?g|png)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string ContainerName = "product-photos";

    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public ProductPhotosController(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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

            var match = FileNamePattern.Match(file.FileName);
            if (!match.Success)
            {
                result.Invalid.Add($"{file.FileName}: doesn't match the \"VendorSku-ColorCode.ext\" naming pattern.");
                continue;
            }

            var extension = "." + match.Groups["ext"].Value.ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                result.Invalid.Add($"{file.FileName}: unsupported file type.");
                continue;
            }

            var vendorSku = match.Groups["sku"].Value;
            var vendorColorCode = match.Groups["color"].Value;

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
            var contentType = extension switch
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

        return View("Index", result);
    }
}
