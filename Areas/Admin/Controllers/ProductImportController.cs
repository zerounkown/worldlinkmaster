using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using static WorldLinkMaster.Web.Areas.Admin.Controllers.ExcelImportHelpers;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Imports WLM_03_Product_Import_Template.xlsx-style workbooks (Products, Product Colors,
/// Variants, Media, Attributes sheets) per WLM_01_Developer_Specification.xlsx. Requires the
/// Master Data importer to have already loaded the Brand/Category/Color/SizeGroup/Size/
/// Attribute codes these sheets reference.
///
/// Storefront display fields (Product.ImageUrl, ProductImages, ProductVariant.ImageUrl) are
/// kept in sync from the imported Media so pages don't go blank, but the actual per-color
/// gallery-swap behavior described in the spec's Media Rules (M001-M009) isn't wired up yet —
/// that's a separate follow-up.
/// </summary>
public class ProductImportController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public ProductImportController(ApplicationDbContext context)
    {
        _context = context;
    }

    public IActionResult Index()
    {
        return View(new ProductImportResult());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        var result = new ProductImportResult();

        if (file == null || file.Length == 0)
        {
            result.Errors.Add("No file was uploaded.");
            return View("Index", result);
        }

        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Only .xlsx files are supported.");
            return View("Index", result);
        }

        XLWorkbook workbook;
        using (var stream = file.OpenReadStream())
        {
            try
            {
                workbook = new XLWorkbook(stream);
            }
            catch (Exception)
            {
                result.Errors.Add("Couldn't parse this file — make sure it's a valid, uncorrupted .xlsx workbook.");
                return View("Index", result);
            }
        }
        using var _ = workbook;

        if (!workbook.Worksheets.Contains("Products"))
        {
            result.Errors.Add("This workbook has no 'Products' sheet — is this the right file?");
            return View("Index", result);
        }

        var defaultMerchant = await _context.Merchants.OrderBy(m => m.Id).FirstOrDefaultAsync();
        if (defaultMerchant == null)
        {
            result.Errors.Add("No merchant account exists to assign imported products to.");
            return View("Index", result);
        }

        var productsBySku = await ImportProductsAsync(workbook.Worksheet("Products"), defaultMerchant, result);

        Dictionary<string, ProductColor> productColorsByCode = new(StringComparer.OrdinalIgnoreCase);
        if (workbook.Worksheets.Contains("Product Colors"))
        {
            productColorsByCode = await ImportProductColorsAsync(workbook.Worksheet("Product Colors"), productsBySku, result);
        }

        if (workbook.Worksheets.Contains("Variants"))
        {
            await ImportVariantsAsync(workbook.Worksheet("Variants"), productsBySku, productColorsByCode, result);
            // The Products sheet has no Stock Qty column (only Variants does), so a newly
            // created product would otherwise sit at StockQuantity=0 forever — which the
            // storefront reads as fully out of stock, hiding the whole Add to Cart form
            // (color/size pickers included), not just the "in stock" message.
            await SyncProductStockFromVariantsAsync(productsBySku);
        }

        var mediaTouchedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (workbook.Worksheets.Contains("Media"))
        {
            mediaTouchedProductCodes = await ImportMediaAsync(workbook.Worksheet("Media"), productsBySku, productColorsByCode, result);
        }

        if (workbook.Worksheets.Contains("Attributes"))
        {
            await ImportAttributesAsync(workbook.Worksheet("Attributes"), productsBySku, result);
        }

        // Keep the existing storefront-visible fields (Product.ImageUrl, ProductImages,
        // ProductVariant.ImageUrl) usable even though the new per-color gallery-swap UI isn't
        // built yet — otherwise imported products would look broken/empty on the site. Scoped to
        // only products with rows in this upload's Media sheet — a product present here just for
        // a price/stock update, with no Media rows of its own, must keep its existing gallery.
        await SyncStorefrontDisplayFieldsAsync(mediaTouchedProductCodes, productsBySku);
        await _context.SaveChangesAsync();

        return View("Index", result);
    }

    private async Task SyncProductStockFromVariantsAsync(Dictionary<string, Product> productsBySku)
    {
        var productIds = productsBySku.Values.Select(p => p.Id).Distinct().ToList();
        if (productIds.Count == 0) return;

        var stockByProductId = await _context.ProductVariants
            .Where(v => productIds.Contains(v.ProductId) && v.Active)
            .GroupBy(v => v.ProductId)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(v => v.StockQuantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Total);

        foreach (var product in productsBySku.Values)
        {
            if (stockByProductId.TryGetValue(product.Id, out var total))
            {
                product.StockQuantity = total;
            }
        }

        await _context.SaveChangesAsync();
    }

    private static bool ShouldProcessRow(string? action, int rowNum, string sheetName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;
        if (action.Equals("ADD", StringComparison.OrdinalIgnoreCase) || action.Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        errors.Add($"{sheetName} row {rowNum}: Action '{action}' isn't supported (only ADD/UPDATE) — row skipped.");
        return false;
    }

    private async Task<Dictionary<string, Product>> ImportProductsAsync(IXLWorksheet sheet, WorldLinkMaster.Web.Models.Merchant defaultMerchant, ProductImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Product Code");
        var nameCol = FindColumn(headers, "Name EN");
        var nameArCol = FindColumn(headers, "Name AR");
        var brandCol = FindColumn(headers, "Brand Code");
        var categoryCol = FindColumn(headers, "Main Category Code");
        var subcategoryCol = FindColumn(headers, "Subcategory Code");
        var sizeGroupCol = FindColumn(headers, "Size Group Code");
        var descCol = FindColumn(headers, "Description EN");
        var descArCol = FindColumn(headers, "Description AR");
        var priceCol = FindColumn(headers, "Default Price Excl. VAT");
        var vatCol = FindColumn(headers, "VAT Rate");
        var statusCol = FindColumn(headers, "Status");
        var featuredCol = FindColumn(headers, "Featured");
        var wholesaleCol = FindColumn(headers, "Wholesale Available");
        var seoTitleCol = FindColumn(headers, "SEO Title EN");
        var seoTitleArCol = FindColumn(headers, "SEO Title AR");
        var slugCol = FindColumn(headers, "URL Slug");

        var productsBySku = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);

        if (codeCol == null || nameCol == null || categoryCol == null || priceCol == null)
        {
            result.Errors.Add("Products sheet: couldn't find required columns (Product Code, Name EN, Main Category Code, Default Price Excl. VAT).");
            return productsBySku;
        }

        var brandsByCode = await _context.Brands.Where(b => b.Code != null).ToDictionaryAsync(b => b.Code!, StringComparer.OrdinalIgnoreCase);
        var categoriesByCode = await _context.Categories.Where(c => c.Code != null).ToDictionaryAsync(c => c.Code!, StringComparer.OrdinalIgnoreCase);
        var subcategoriesByCode = await _context.Subcategories.Where(s => s.Code != null).ToDictionaryAsync(s => s.Code!, StringComparer.OrdinalIgnoreCase);
        var sizeGroupsByCode = await _context.SizeGroups.ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase);
        var existing = await _context.Products.ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase);
        var usedSlugs = new HashSet<string>(await _context.Products.Select(p => p.Slug).ToListAsync(), StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Products", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            var categoryCode = GetString(sheet, rowNum, categoryCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(categoryCode))
            {
                result.Errors.Add($"Products row {rowNum}: Product Code, Name EN, and Main Category Code are required.");
                continue;
            }
            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Products row {rowNum} (P001): duplicate Product Code '{code}' in this sheet.");
                continue;
            }
            if (!TryReadDecimal(sheet, rowNum, priceCol, out var price) || price < 0)
            {
                result.Errors.Add($"Products row {rowNum} (SKU {code}): invalid Default Price Excl. VAT value.");
                continue;
            }
            if (!categoriesByCode.TryGetValue(categoryCode, out var category))
            {
                var suggestion = FindClosestMatch(categoryCode, categoriesByCode.Keys);
                result.Errors.Add(WithSuggestion($"Products row {rowNum} (SKU {code}): Main Category Code '{categoryCode}' doesn't match any category — import the Master Data sheet first.", suggestion));
                continue;
            }

            int? brandId = null;
            var brandCode = GetString(sheet, rowNum, brandCol);
            if (brandCode != null)
            {
                if (!brandsByCode.TryGetValue(brandCode, out var brand))
                {
                    var suggestion = FindClosestMatch(brandCode, brandsByCode.Keys);
                    result.Errors.Add(WithSuggestion($"Products row {rowNum} (SKU {code}): Brand Code '{brandCode}' doesn't match any brand.", suggestion));
                    continue;
                }
                brandId = brand.Id;
            }

            int? subcategoryId = null;
            var subcategoryCode = GetString(sheet, rowNum, subcategoryCol);
            if (subcategoryCode != null)
            {
                if (!subcategoriesByCode.TryGetValue(subcategoryCode, out var subcategory))
                {
                    var suggestion = FindClosestMatch(subcategoryCode, subcategoriesByCode.Keys);
                    result.Errors.Add(WithSuggestion($"Products row {rowNum} (SKU {code}): Subcategory Code '{subcategoryCode}' doesn't match any subcategory.", suggestion));
                    continue;
                }
                if (subcategory.CategoryId != category.Id)
                {
                    result.Errors.Add($"Products row {rowNum} (SKU {code}): Subcategory '{subcategoryCode}' doesn't belong to Category '{categoryCode}'.");
                    continue;
                }
                subcategoryId = subcategory.Id;
            }

            int? sizeGroupId = null;
            var sizeGroupCode = GetString(sheet, rowNum, sizeGroupCol);
            if (sizeGroupCode != null)
            {
                if (!sizeGroupsByCode.TryGetValue(sizeGroupCode, out var sizeGroup))
                {
                    var suggestion = FindClosestMatch(sizeGroupCode, sizeGroupsByCode.Keys);
                    result.Errors.Add(WithSuggestion($"Products row {rowNum} (SKU {code}): Size Group Code '{sizeGroupCode}' doesn't match any size group.", suggestion));
                    continue;
                }
                sizeGroupId = sizeGroup.Id;
            }

            decimal? vatRate = TryReadDecimal(sheet, rowNum, vatCol, out var vr) ? vr : null;
            var status = GetString(sheet, rowNum, statusCol);
            var isPublished = status == null || status.Equals("Published", StringComparison.OrdinalIgnoreCase);
            var featured = ReadYesNo(sheet, rowNum, featuredCol, false);
            var wholesaleAvailable = ReadYesNo(sheet, rowNum, wholesaleCol, false);
            var seoTitleEn = GetString(sheet, rowNum, seoTitleCol);
            var seoTitleAr = GetString(sheet, rowNum, seoTitleArCol);
            var explicitSlug = GetString(sheet, rowNum, slugCol);
            var roundedPrice = Math.Round(price, 2);

            if (existing.TryGetValue(code, out var product))
            {
                product.Name = name;
                product.NameAr = GetString(sheet, rowNum, nameArCol);
                product.BrandId = brandId;
                product.CategoryId = category.Id;
                product.SubcategoryId = subcategoryId;
                product.SizeGroupId = sizeGroupId;
                product.Description = GetString(sheet, rowNum, descCol);
                product.DescriptionAr = GetString(sheet, rowNum, descArCol);
                product.Price = roundedPrice;
                product.VatRate = vatRate;
                product.IsPublished = isPublished;
                product.IsFeatured = featured;
                product.WholesalePrice = wholesaleAvailable ? roundedPrice : null;
                product.SeoTitleEn = seoTitleEn;
                product.SeoTitleAr = seoTitleAr;
                if (explicitSlug != null && !explicitSlug.Equals(product.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    usedSlugs.Remove(product.Slug);
                    product.Slug = UniqueSlug(explicitSlug, usedSlugs);
                }
                result.ProductsUpdated++;
            }
            else
            {
                product = new Product
                {
                    Sku = code,
                    Name = name,
                    NameAr = GetString(sheet, rowNum, nameArCol),
                    BrandId = brandId,
                    CategoryId = category.Id,
                    SubcategoryId = subcategoryId,
                    SizeGroupId = sizeGroupId,
                    Description = GetString(sheet, rowNum, descCol),
                    DescriptionAr = GetString(sheet, rowNum, descArCol),
                    Price = roundedPrice,
                    VatRate = vatRate,
                    IsPublished = isPublished,
                    IsFeatured = featured,
                    WholesalePrice = wholesaleAvailable ? roundedPrice : null,
                    SeoTitleEn = seoTitleEn,
                    SeoTitleAr = seoTitleAr,
                    Slug = UniqueSlug(explicitSlug ?? name, usedSlugs),
                    MerchantId = defaultMerchant.Id,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Products.Add(product);
                existing[code] = product;
                result.ProductsCreated++;
            }

            productsBySku[code] = product;
        }

        await _context.SaveChangesAsync();
        return productsBySku;
    }

    private async Task<Dictionary<string, ProductColor>> ImportProductColorsAsync(
        IXLWorksheet sheet, Dictionary<string, Product> productsBySku, ProductImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Product Color Code");
        var productCol = FindColumn(headers, "Product Code");
        var colorCol = FindColumn(headers, "Color Code");
        var orderCol = FindColumn(headers, "Color Display Order");
        var defaultCol = FindColumn(headers, "Default Color");
        var swatchCol = FindColumn(headers, "Swatch Image URL");
        var activeCol = FindColumn(headers, "Active");

        var productColorsByCode = new Dictionary<string, ProductColor>(StringComparer.OrdinalIgnoreCase);

        if (codeCol == null || productCol == null || colorCol == null)
        {
            result.Errors.Add("Product Colors sheet: couldn't find required columns (Product Color Code, Product Code, Color Code).");
            return productColorsByCode;
        }

        var colorsByCode = await _context.Colors.Where(c => c.Code != null).ToDictionaryAsync(c => c.Code!, StringComparer.OrdinalIgnoreCase);
        var existing = await _context.ProductColors.ToDictionaryAsync(pc => pc.Code, StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultCountByProduct = new Dictionary<int, int>();
        var productCodeById = productsBySku.ToDictionary(kv => kv.Value.Id, kv => kv.Key);
        // Display Order gaps/duplicates are checked against just this file's rows per product,
        // not the product's full saved color set — the common case is a product's whole color
        // lineup being defined together in one file, and that's what this validates.
        var displayOrdersByProduct = new Dictionary<int, List<int>>();

        // Pre-count defaults already saved (for other colors not touched by this sheet) so a
        // new default in the file can be caught as a PC002 conflict against existing data too.
        foreach (var pc in existing.Values.Where(pc => pc.DefaultColor))
        {
            defaultCountByProduct[pc.ProductId] = defaultCountByProduct.GetValueOrDefault(pc.ProductId) + 1;
        }

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Product Colors", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var productCode = GetString(sheet, rowNum, productCol);
            var colorCode = GetString(sheet, rowNum, colorCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(colorCode))
            {
                result.Errors.Add($"Product Colors row {rowNum}: Product Color Code, Product Code, and Color Code are required.");
                continue;
            }
            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Product Colors row {rowNum} (PC001): duplicate Product Color Code '{code}' in this sheet.");
                continue;
            }
            if (!productsBySku.TryGetValue(productCode, out var product))
            {
                result.Errors.Add($"Product Colors row {rowNum} (Code {code}): Product Code '{productCode}' doesn't match any product in the Products sheet or the catalog.");
                continue;
            }
            if (!colorsByCode.TryGetValue(colorCode, out var color))
            {
                var suggestion = FindClosestMatch(colorCode, colorsByCode.Keys);
                result.Errors.Add(WithSuggestion($"Product Colors row {rowNum} (Code {code}): Color Code '{colorCode}' doesn't match any color — import the Master Data sheet first.", suggestion));
                continue;
            }

            var isDefault = ReadYesNo(sheet, rowNum, defaultCol, false);
            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);
            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            var swatchUrl = GetString(sheet, rowNum, swatchCol);

            if (existing.TryGetValue(code, out var productColor))
            {
                if (isDefault && !productColor.DefaultColor && defaultCountByProduct.GetValueOrDefault(product.Id) > 0)
                {
                    result.Errors.Add($"Product Colors row {rowNum} (PC002): Product '{productCode}' already has a default color — only one is allowed.");
                    continue;
                }
                if (productColor.DefaultColor && !isDefault)
                {
                    defaultCountByProduct[product.Id] = Math.Max(0, defaultCountByProduct.GetValueOrDefault(product.Id) - 1);
                }
                productColor.ProductId = product.Id;
                productColor.ColorId = color.Id;
                productColor.DisplayOrder = displayOrder;
                productColor.DefaultColor = isDefault;
                productColor.SwatchImageUrl = swatchUrl;
                productColor.Active = active;
                result.ProductColorsUpdated++;
            }
            else
            {
                if (isDefault && defaultCountByProduct.GetValueOrDefault(product.Id) > 0)
                {
                    result.Errors.Add($"Product Colors row {rowNum} (PC002): Product '{productCode}' already has a default color — only one is allowed.");
                    continue;
                }
                productColor = new ProductColor
                {
                    Code = code,
                    ProductId = product.Id,
                    ColorId = color.Id,
                    DisplayOrder = displayOrder,
                    DefaultColor = isDefault,
                    SwatchImageUrl = swatchUrl,
                    Active = active
                };
                _context.ProductColors.Add(productColor);
                existing[code] = productColor;
                result.ProductColorsCreated++;
            }

            if (isDefault)
            {
                defaultCountByProduct[product.Id] = defaultCountByProduct.GetValueOrDefault(product.Id) + 1;
            }

            if (!displayOrdersByProduct.TryGetValue(product.Id, out var orders))
            {
                orders = new List<int>();
                displayOrdersByProduct[product.Id] = orders;
            }
            orders.Add(displayOrder);

            productColorsByCode[code] = productColor;
        }

        foreach (var (productId, orders) in displayOrdersByProduct)
        {
            var productCode = productCodeById.GetValueOrDefault(productId, productId.ToString());

            if (defaultCountByProduct.GetValueOrDefault(productId) == 0)
            {
                result.Errors.Add($"Product Colors (PC002): Product '{productCode}' has no color marked Default Color — exactly one is required.");
            }

            var sorted = orders.OrderBy(o => o).ToList();
            var expected = Enumerable.Range(1, sorted.Count).ToList();
            if (!sorted.SequenceEqual(expected))
            {
                var duplicates = sorted.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                var detail = duplicates.Count > 0
                    ? $"duplicate value(s) {string.Join(", ", duplicates)}"
                    : $"got [{string.Join(", ", sorted)}], expected 1..{sorted.Count} with no gaps";
                result.Errors.Add($"Product Colors: Product '{productCode}' has non-sequential Color Display Order values ({detail}).");
            }
        }

        await _context.SaveChangesAsync();
        return productColorsByCode;
    }

    private async Task ImportVariantsAsync(
        IXLWorksheet sheet, Dictionary<string, Product> productsBySku,
        Dictionary<string, ProductColor> productColorsByCode, ProductImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var productCol = FindColumn(headers, "Product Code");
        var productColorCol = FindColumn(headers, "Product Color Code");
        var skuCol = FindColumn(headers, "Variant SKU");
        var barcodeCol = FindColumn(headers, "Barcode");
        var sizeCol = FindColumn(headers, "Size Code");
        var priceCol = FindColumn(headers, "Price Excl. VAT");
        var vatCol = FindColumn(headers, "VAT Rate");
        var stockCol = FindColumn(headers, "Stock Qty");
        var alertCol = FindColumn(headers, "Alert Qty");
        var backorderCol = FindColumn(headers, "Allow Backorder");
        var activeCol = FindColumn(headers, "Active");

        if (skuCol == null || productCol == null || productColorCol == null || stockCol == null)
        {
            result.Errors.Add("Variants sheet: couldn't find required columns (Product Code, Product Color Code, Variant SKU, Stock Qty).");
            return;
        }

        var sizesByCode = await _context.Sizes.Where(s => s.Code != null).ToDictionaryAsync(s => s.Code!, StringComparer.OrdinalIgnoreCase);
        var existing = await _context.ProductVariants.ToDictionaryAsync(v => v.Sku, StringComparer.OrdinalIgnoreCase);
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBarcodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingBarcodes = new HashSet<string>(
            existing.Values.Where(v => v.Barcode != null).Select(v => v.Barcode!), StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Variants", result.Errors)) continue;

            var sku = GetString(sheet, rowNum, skuCol);
            var productCode = GetString(sheet, rowNum, productCol);
            var productColorCode = GetString(sheet, rowNum, productColorCol);
            if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(productColorCode))
            {
                result.Errors.Add($"Variants row {rowNum}: Variant SKU, Product Code, and Product Color Code are required.");
                continue;
            }
            if (!seenSkus.Add(sku))
            {
                result.Errors.Add($"Variants row {rowNum} (V001): duplicate Variant SKU '{sku}' in this sheet.");
                continue;
            }
            if (!TryReadInt(sheet, rowNum, stockCol, out var stock) || stock < 0)
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {sku}): invalid Stock Qty value.");
                continue;
            }
            if (!productsBySku.TryGetValue(productCode, out var product))
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {sku}): Product Code '{productCode}' doesn't match any product.");
                continue;
            }
            if (!productColorsByCode.TryGetValue(productColorCode, out var productColor))
            {
                // Product Color Code matching is already case-insensitive (a plain casing slip
                // like "nav" vs "NAV" resolves before this point) — landing here means the code
                // genuinely doesn't exist, most often a typo like "oli" vs "OLV".
                var suggestion = FindClosestMatch(productColorCode, productColorsByCode.Keys);
                result.Errors.Add(WithSuggestion($"Variants row {rowNum} (SKU {sku}): Product Color Code '{productColorCode}' doesn't match any product color.", suggestion));
                continue;
            }

            Size? size = null;
            var sizeCode = GetString(sheet, rowNum, sizeCol);
            if (sizeCode != null)
            {
                if (!sizesByCode.TryGetValue(sizeCode, out size))
                {
                    var suggestion = FindClosestMatch(sizeCode, sizesByCode.Keys);
                    result.Errors.Add(WithSuggestion($"Variants row {rowNum} (SKU {sku}): Size Code '{sizeCode}' doesn't match any size — import the Master Data sheet first.", suggestion));
                    continue;
                }
                if (product.SizeGroupId != null && size.SizeGroupId != product.SizeGroupId)
                {
                    result.Errors.Add($"Variants row {rowNum} (V003, SKU {sku}): Size '{sizeCode}' doesn't belong to this product's Size Group.");
                    continue;
                }
            }

            var barcode = GetString(sheet, rowNum, barcodeCol);
            if (barcode != null)
            {
                var alreadyUsedByOther = !existing.TryGetValue(sku, out var self) || self.Barcode != barcode;
                if (!seenBarcodes.Add(barcode))
                {
                    result.Errors.Add($"Variants row {rowNum} (V002, SKU {sku}): duplicate Barcode '{barcode}' in this sheet.");
                    continue;
                }
                if (alreadyUsedByOther && existingBarcodes.Contains(barcode))
                {
                    result.Errors.Add($"Variants row {rowNum} (V002, SKU {sku}): Barcode '{barcode}' is already used by another variant.");
                    continue;
                }
            }

            decimal? price = TryReadDecimal(sheet, rowNum, priceCol, out var p) ? Math.Round(p, 2) : null;
            decimal? vatRate = TryReadDecimal(sheet, rowNum, vatCol, out var vr) ? vr : null;
            int? alertQty = TryReadInt(sheet, rowNum, alertCol, out var aq) ? aq : null;
            var allowBackorder = ReadYesNo(sheet, rowNum, backorderCol, false);
            var active = ReadYesNo(sheet, rowNum, activeCol, true);

            if (existing.TryGetValue(sku, out var variant))
            {
                variant.ProductId = product.Id;
                variant.ProductColorId = productColor.Id;
                variant.ColorId = productColor.ColorId;
                variant.SizeId = size?.Id;
                variant.Barcode = barcode;
                variant.Price = price;
                variant.VatRate = vatRate;
                variant.StockQuantity = stock;
                variant.AlertQty = alertQty;
                variant.AllowBackorder = allowBackorder;
                variant.Active = active;
                result.VariantsUpdated++;
            }
            else
            {
                variant = new ProductVariant
                {
                    ProductId = product.Id,
                    ProductColorId = productColor.Id,
                    ColorId = productColor.ColorId,
                    SizeId = size?.Id,
                    Sku = sku,
                    Barcode = barcode,
                    Price = price,
                    VatRate = vatRate,
                    StockQuantity = stock,
                    AlertQty = alertQty,
                    AllowBackorder = allowBackorder,
                    Active = active
                };
                _context.ProductVariants.Add(variant);
                existing[sku] = variant;
                result.VariantsCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task<HashSet<string>> ImportMediaAsync(
        IXLWorksheet sheet, Dictionary<string, Product> productsBySku,
        Dictionary<string, ProductColor> productColorsByCode, ProductImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var productCol = FindColumn(headers, "Product Code");
        var productColorCol = FindColumn(headers, "Product Color Code");
        var scopeCol = FindColumn(headers, "Media Scope");
        var typeCol = FindColumn(headers, "Media Type");
        var roleCol = FindColumn(headers, "Media Role");
        var urlCol = FindColumn(headers, "Media URL");
        var thumbCol = FindColumn(headers, "Thumbnail URL");
        var orderCol = FindColumn(headers, "Display Order");
        var mainCol = FindColumn(headers, "Is Color Main");
        var galleryCol = FindColumn(headers, "Show in Gallery");
        var altEnCol = FindColumn(headers, "Alt Text EN");
        var altArCol = FindColumn(headers, "Alt Text AR");
        var activeCol = FindColumn(headers, "Active");

        var touchedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (productCol == null || urlCol == null || scopeCol == null)
        {
            result.Errors.Add("Media sheet: couldn't find required columns (Product Code, Media Scope, Media URL).");
            return touchedProductCodes;
        }

        // Rows have no stable natural key to match against for an update, so each product
        // appearing in this sheet gets its media fully replaced by what's in the file.
        var newMediaByProduct = new Dictionary<int, List<ProductMedia>>();
        var mainCountByColor = new Dictionary<int, int>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Media", result.Errors)) continue;

            var productCode = GetString(sheet, rowNum, productCol);
            var url = GetString(sheet, rowNum, urlCol);
            var scope = GetString(sheet, rowNum, scopeCol);
            if (string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(scope))
            {
                result.Errors.Add($"Media row {rowNum}: Product Code and Media Scope are required.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(url))
            {
                result.Errors.Add($"Media row {rowNum} (M004, Product {productCode}): Media URL is required.");
                continue;
            }
            if (!productsBySku.TryGetValue(productCode, out var product))
            {
                result.Errors.Add($"Media row {rowNum}: Product Code '{productCode}' doesn't match any product.");
                continue;
            }

            var productColorCode = GetString(sheet, rowNum, productColorCol);
            int? productColorId = null;
            if (scope.Equals("Shared", StringComparison.OrdinalIgnoreCase))
            {
                if (productColorCode != null)
                {
                    result.Errors.Add($"Media row {rowNum} (M002, Product {productCode}): Shared media must leave Product Color Code blank.");
                    continue;
                }
            }
            else if (scope.Equals("Color", StringComparison.OrdinalIgnoreCase))
            {
                if (productColorCode == null)
                {
                    result.Errors.Add($"Media row {rowNum} (M003, Product {productCode}): Color-scoped media needs a Product Color Code.");
                    continue;
                }
                if (!productColorsByCode.TryGetValue(productColorCode, out var productColor) || productColor.ProductId != product.Id)
                {
                    var suggestion = FindClosestMatch(productColorCode, productColorsByCode.Keys);
                    result.Errors.Add(WithSuggestion($"Media row {rowNum} (Product {productCode}): Product Color Code '{productColorCode}' doesn't match a color of this product.", suggestion));
                    continue;
                }
                productColorId = productColor.Id;
            }
            else
            {
                result.Errors.Add($"Media row {rowNum} (Product {productCode}): Media Scope must be 'Shared' or 'Color', got '{scope}'.");
                continue;
            }

            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);
            var isMain = ReadYesNo(sheet, rowNum, mainCol, false);
            var media = new ProductMedia
            {
                ProductId = product.Id,
                ProductColorId = productColorId,
                MediaScope = scope,
                MediaType = GetString(sheet, rowNum, typeCol) ?? "Image",
                MediaRole = GetString(sheet, rowNum, roleCol),
                MediaUrl = url,
                ThumbnailUrl = GetString(sheet, rowNum, thumbCol),
                DisplayOrder = displayOrder,
                IsColorMain = isMain,
                ShowInGallery = ReadYesNo(sheet, rowNum, galleryCol, true),
                AltTextEn = GetString(sheet, rowNum, altEnCol),
                AltTextAr = GetString(sheet, rowNum, altArCol),
                Active = ReadYesNo(sheet, rowNum, activeCol, true)
            };

            if (!newMediaByProduct.TryGetValue(product.Id, out var list))
            {
                list = new List<ProductMedia>();
                newMediaByProduct[product.Id] = list;
            }
            list.Add(media);
            touchedProductCodes.Add(productCode);

            if (isMain && media.Active && productColorId.HasValue)
            {
                mainCountByColor[productColorId.Value] = mainCountByColor.GetValueOrDefault(productColorId.Value) + 1;
            }
        }

        foreach (var (colorId, count) in mainCountByColor)
        {
            if (count > 1)
            {
                var code = productColorsByCode.Values.FirstOrDefault(pc => pc.Id == colorId)?.Code ?? colorId.ToString();
                result.Errors.Add($"Media (M001): Product Color '{code}' has {count} rows marked Is Color Main — only one is allowed.");
            }
        }

        if (newMediaByProduct.Count == 0)
        {
            return touchedProductCodes;
        }

        var productIds = newMediaByProduct.Keys.ToList();
        var oldMedia = await _context.ProductMedia.Where(m => productIds.Contains(m.ProductId)).ToListAsync();
        _context.ProductMedia.RemoveRange(oldMedia);
        foreach (var list in newMediaByProduct.Values)
        {
            _context.ProductMedia.AddRange(list);
            result.MediaWritten += list.Count;
        }

        await _context.SaveChangesAsync();
        return touchedProductCodes;
    }

    private async Task ImportAttributesAsync(IXLWorksheet sheet, Dictionary<string, Product> productsBySku, ProductImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var productCol = FindColumn(headers, "Product Code");
        var attrCol = FindColumn(headers, "Attribute Code");
        var valueEnCol = FindColumn(headers, "Value EN");
        var valueArCol = FindColumn(headers, "Value AR");
        var filterCol = FindColumn(headers, "Use in Filter");
        var orderCol = FindColumn(headers, "Display Order");
        var activeCol = FindColumn(headers, "Active");

        if (productCol == null || attrCol == null || valueEnCol == null)
        {
            result.Errors.Add("Attributes sheet: couldn't find required columns (Product Code, Attribute Code, Value EN).");
            return;
        }

        var attributesByCode = await _context.AttributeDefinitions.ToDictionaryAsync(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var newValuesByProduct = new Dictionary<int, List<ProductAttributeValue>>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Attributes", result.Errors)) continue;

            var productCode = GetString(sheet, rowNum, productCol);
            var attrCode = GetString(sheet, rowNum, attrCol);
            var valueEn = GetString(sheet, rowNum, valueEnCol);
            if (string.IsNullOrWhiteSpace(productCode) || string.IsNullOrWhiteSpace(attrCode) || string.IsNullOrWhiteSpace(valueEn))
            {
                result.Errors.Add($"Attributes row {rowNum}: Product Code, Attribute Code, and Value EN are required.");
                continue;
            }
            if (!productsBySku.TryGetValue(productCode, out var product))
            {
                result.Errors.Add($"Attributes row {rowNum}: Product Code '{productCode}' doesn't match any product.");
                continue;
            }
            if (!attributesByCode.TryGetValue(attrCode, out var attrDef))
            {
                var suggestion = FindClosestMatch(attrCode, attributesByCode.Keys);
                result.Errors.Add(WithSuggestion($"Attributes row {rowNum} (Product {productCode}): Attribute Code '{attrCode}' doesn't match any attribute — import the Master Data sheet first.", suggestion));
                continue;
            }

            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);
            var value = new ProductAttributeValue
            {
                ProductId = product.Id,
                AttributeDefinitionId = attrDef.Id,
                ValueEn = valueEn,
                ValueAr = GetString(sheet, rowNum, valueArCol),
                UseInFilter = ReadYesNo(sheet, rowNum, filterCol, false),
                DisplayOrder = displayOrder,
                Active = ReadYesNo(sheet, rowNum, activeCol, true)
            };

            if (!newValuesByProduct.TryGetValue(product.Id, out var list))
            {
                list = new List<ProductAttributeValue>();
                newValuesByProduct[product.Id] = list;
            }
            list.Add(value);
        }

        if (newValuesByProduct.Count == 0)
        {
            return;
        }

        var productIds = newValuesByProduct.Keys.ToList();
        var oldValues = await _context.ProductAttributeValues.Where(v => productIds.Contains(v.ProductId)).ToListAsync();
        _context.ProductAttributeValues.RemoveRange(oldValues);
        foreach (var list in newValuesByProduct.Values)
        {
            _context.ProductAttributeValues.AddRange(list);
            result.AttributesWritten += list.Count;
        }

        await _context.SaveChangesAsync();
    }

    private async Task SyncStorefrontDisplayFieldsAsync(HashSet<string> touchedProductCodes, Dictionary<string, Product> productsBySku)
    {
        if (touchedProductCodes.Count == 0) return;

        var productIds = touchedProductCodes
            .Where(productsBySku.ContainsKey)
            .Select(code => productsBySku[code].Id)
            .Distinct()
            .ToList();

        var media = await _context.ProductMedia
            .Where(m => productIds.Contains(m.ProductId) && m.Active && m.MediaType == "Image")
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync();
        if (media.Count == 0) return;

        var productColors = await _context.ProductColors
            .Where(pc => productIds.Contains(pc.ProductId))
            .ToListAsync();

        var existingImages = await _context.ProductImages.Where(pi => productIds.Contains(pi.ProductId)).ToListAsync();
        _context.ProductImages.RemoveRange(existingImages);

        var variants = await _context.ProductVariants
            .Where(v => productIds.Contains(v.ProductId) && v.ProductColorId != null)
            .ToListAsync();

        foreach (var productId in productIds)
        {
            var productMedia = media.Where(m => m.ProductId == productId).ToList();
            if (productMedia.Count == 0) continue;

            var defaultColor = productColors.FirstOrDefault(pc => pc.ProductId == productId && pc.DefaultColor)
                ?? productColors.FirstOrDefault(pc => pc.ProductId == productId);

            var sharedImages = productMedia.Where(m => m.ProductColorId == null).OrderBy(m => m.DisplayOrder).ToList();
            var defaultColorImages = defaultColor == null
                ? new List<ProductMedia>()
                : productMedia.Where(m => m.ProductColorId == defaultColor.Id).OrderBy(m => m.DisplayOrder).ToList();

            var galleryImages = sharedImages.Concat(defaultColorImages).ToList();
            if (galleryImages.Count == 0) continue;

            var product = productsBySku.Values.First(p => p.Id == productId);
            product.ImageUrl = galleryImages[0].MediaUrl;

            var sortOrder = 0;
            foreach (var img in galleryImages)
            {
                _context.ProductImages.Add(new ProductImage
                {
                    ProductId = productId,
                    ImageUrl = img.MediaUrl,
                    Label = img.MediaRole,
                    SortOrder = sortOrder++
                });
            }

            // Per-color variant image: that color's main image, else its swatch, else unchanged.
            foreach (var colorGroup in productColors.Where(pc => pc.ProductId == productId))
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
    }
}
