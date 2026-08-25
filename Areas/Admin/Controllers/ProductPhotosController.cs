using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Bulk photo upload for vendor products. Filenames are expected in the vendor's own
/// "{VendorSku}-{VendorColorCode}[-suffix].ext" convention (e.g. Condor's "101228-002.webp").
/// Each file is matched to a Product by Product.VendorSku and a ProductColor by
/// (ProductId, VendorColorCode) — both populated by the Product/Master Data importers or a
/// one-off backfill, not by this tool. Files that don't match are reported, not guessed at.
/// </summary>
public class ProductPhotosController : AdminBaseController
{
    private const long MaxFileSizeBytes = 15 * 1024 * 1024; // 15 MB
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webp", ".jpg", ".jpeg", ".png"
    };
    private static readonly Regex FileNamePattern = new(
        @"^(?<sku>\d{4,8})-(?<color>\d{2,4})(?:-[A-Za-z0-9]+)?\.(?<ext>webp|jpe?g|png)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string PlaceholderMediaUrl = "TBD - needs hosted URL";
    private const string UploadRelativeFolder = "images/products/condor";

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public ProductPhotosController(ApplicationDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
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

        var uploadRoot = Path.Combine(_environment.WebRootPath, "images", "products", "condor");
        Directory.CreateDirectory(uploadRoot);

        var touchedProductIds = new HashSet<int>();

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
            var fullPath = Path.Combine(uploadRoot, safeFileName);
            await using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }
            var mediaUrl = $"/{UploadRelativeFolder}/{safeFileName}";

            var existingMedia = await _context.ProductMedia
                .Where(m => m.ProductId == product.Id && m.ProductColorId == productColor.Id)
                .ToListAsync();

            var alreadyPresent = existingMedia.Any(m => m.MediaUrl == mediaUrl);
            var placeholder = existingMedia.FirstOrDefault(m => m.MediaUrl == PlaceholderMediaUrl);
            var hasRealMedia = existingMedia.Any(m => m.MediaUrl != PlaceholderMediaUrl);

            var replaced = false;
            if (alreadyPresent)
            {
                // Re-upload of the same file — nothing to do.
            }
            else if (placeholder != null)
            {
                placeholder.MediaUrl = mediaUrl;
                placeholder.IsColorMain = true;
                replaced = true;
            }
            else
            {
                var maxOrder = existingMedia.Count == 0 ? 0 : existingMedia.Max(m => m.DisplayOrder);
                _context.ProductMedia.Add(new ProductMedia
                {
                    ProductId = product.Id,
                    ProductColorId = productColor.Id,
                    MediaScope = "Color",
                    MediaType = "Image",
                    MediaUrl = mediaUrl,
                    DisplayOrder = maxOrder + 1,
                    IsColorMain = !hasRealMedia,
                    ShowInGallery = true,
                    Active = true
                });
            }

            touchedProductIds.Add(product.Id);
            result.Matched.Add(new ProductPhotoUploadResult.MatchedPhoto(
                file.FileName, product.Name, product.Sku, productColor.Color?.Name ?? "", mediaUrl, replaced));
        }

        await _context.SaveChangesAsync();
        await SyncStorefrontDisplayFieldsAsync(touchedProductIds);

        return View("Index", result);
    }

    // Mirrors ProductImportController.SyncStorefrontDisplayFieldsAsync, scoped to just the
    // products this upload touched, so listing/PDP image fields don't go stale between a full
    // Product Import run and incremental photo uploads like this one.
    private async Task SyncStorefrontDisplayFieldsAsync(HashSet<int> productIds)
    {
        if (productIds.Count == 0) return;

        var products = await _context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        var media = await _context.ProductMedia
            .Where(m => productIds.Contains(m.ProductId) && m.Active && m.MediaType == "Image")
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();
        var productColors = await _context.ProductColors.Where(pc => productIds.Contains(pc.ProductId)).ToListAsync();
        var existingImages = await _context.ProductImages.Where(pi => productIds.Contains(pi.ProductId)).ToListAsync();
        _context.ProductImages.RemoveRange(existingImages);
        var variants = await _context.ProductVariants
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
                    _context.ProductImages.Add(new ProductImage
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

        await _context.SaveChangesAsync();
    }
}
