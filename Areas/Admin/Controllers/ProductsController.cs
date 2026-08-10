using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class ProductsController : AdminBaseController
{
    private const int PageSize = 25;

    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ProductsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = _context.Products.Include(p => p.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return View(new AdminProductListViewModel
        {
            Products = products,
            SearchTerm = search,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount
        });
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Subcategories = await _context.Subcategories.OrderBy(s => s.Name).ToListAsync();
        return View(new Product());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Subcategories = await _context.Subcategories.OrderBy(s => s.Name).ToListAsync();
            return View(product);
        }

        // The form has no Merchant field (there's only ever one merchant account behind the
        // Admin catalog) — assign it here rather than trusting a client-submitted value, since
        // leaving it unset defaults to 0 and violates the Products→Merchants foreign key.
        var defaultMerchant = await _context.Merchants.OrderBy(m => m.Id).FirstOrDefaultAsync();
        if (defaultMerchant == null)
        {
            ModelState.AddModelError(string.Empty, _localizer["No merchant account exists to assign this product to."]);
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Subcategories = await _context.Subcategories.OrderBy(s => s.Name).ToListAsync();
            return View(product);
        }

        product.MerchantId = defaultMerchant.Id;
        product.CreatedAt = DateTime.UtcNow;
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Product '{0}' created.", product.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return NotFound();
        }

        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Subcategories = await _context.Subcategories.OrderBy(s => s.Name).ToListAsync();
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product product)
    {
        if (id != product.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Subcategories = await _context.Subcategories.OrderBy(s => s.Name).ToListAsync();
            return View(product);
        }

        // Field-by-field update on the tracked entity — not a blind Update(product) on the
        // detached, form-bound object. The form has no fields for Merchant/Rating/ReviewCount,
        // so overwriting the whole entity would reset those to 0 (and MerchantId=0 violates the
        // Products→Merchants foreign key, since there's no merchant with that id).
        var existing = await _context.Products.FindAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.Name = product.Name;
        existing.Slug = product.Slug;
        existing.CategoryId = product.CategoryId;
        existing.SubcategoryId = product.SubcategoryId;
        existing.ShortDescription = product.ShortDescription;
        existing.Description = product.Description;
        existing.Price = product.Price;
        existing.WholesalePrice = product.WholesalePrice;
        existing.Sku = product.Sku;
        existing.StockQuantity = product.StockQuantity;
        existing.ImageUrl = product.ImageUrl;
        existing.IsFeatured = product.IsFeatured;

        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Product '{0}' updated.", existing.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var product = await _context.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id);
        if (product == null)
        {
            return NotFound();
        }

        ViewBag.OrderCount = await _context.OrderItems.CountAsync(oi => oi.ProductId == id);

        return View(product);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Products that have ever been ordered can't be hard-deleted (the order history
        // references them). Setting stock to 0 hides it from the storefront instead, without
        // breaking past orders.
        var hasOrders = await _context.OrderItems.AnyAsync(oi => oi.ProductId == id);
        if (hasOrders)
        {
            TempData["AdminMessage"] = _localizer["Can't delete '{0}' — it has order history. Set its stock to 0 instead (Edit) to retire it without breaking past orders.", product.Name].Value;
            return RedirectToAction(nameof(Index));
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Product '{0}' deleted.", product.Name].Value;

        return RedirectToAction(nameof(Index));
    }

    private static readonly string[] ExcelHeaders = { "Sku", "Name", "Category", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };
    private static readonly string[] VariantExcelHeaders = { "Product Sku (Parent)", "Variant Sku", "Color", "Size", "Price (AED)", "Wholesale Price (AED)", "Stock Quantity", "Image URL" };

    /// <summary>Downloads the full catalog as an .xlsx (Products + Variants sheets) — the same file layout <see cref="BulkUpdate(IFormFile?)"/> expects back.</summary>
    public async Task<IActionResult> ExportExcel()
    {
        var products = await _context.Products
            .Include(p => p.Category)
            .OrderBy(p => p.Category!.Name).ThenBy(p => p.Name)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");

        for (var i = 0; i < ExcelHeaders.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = ExcelHeaders[i];
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var product in products)
        {
            sheet.Cell(row, 1).Value = product.Sku;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.Category?.Name;
            sheet.Cell(row, 4).Value = product.Price;
            if (product.WholesalePrice.HasValue)
            {
                sheet.Cell(row, 5).Value = product.WholesalePrice.Value;
            }
            sheet.Cell(row, 6).Value = product.StockQuantity;
            sheet.Cell(row, 7).Value = product.ImageUrl;
            row++;
        }

        sheet.Columns(1, ExcelHeaders.Length).AdjustToContents();
        sheet.Column(2).Width = Math.Min(sheet.Column(2).Width, 45);

        var variants = await _context.ProductVariants
            .Include(v => v.Product)
            .Include(v => v.Color)
            .Include(v => v.Size)
            .OrderBy(v => v.Product!.Sku).ThenBy(v => v.Sku)
            .ToListAsync();

        var variantSheet = workbook.Worksheets.Add("Variants");
        for (var i = 0; i < VariantExcelHeaders.Length; i++)
        {
            variantSheet.Cell(1, i + 1).Value = VariantExcelHeaders[i];
        }
        variantSheet.Row(1).Style.Font.Bold = true;
        variantSheet.SheetView.FreezeRows(1);

        var vRow = 2;
        foreach (var variant in variants)
        {
            variantSheet.Cell(vRow, 1).Value = variant.Product?.Sku;
            variantSheet.Cell(vRow, 2).Value = variant.Sku;
            variantSheet.Cell(vRow, 3).Value = variant.Color?.Name;
            variantSheet.Cell(vRow, 4).Value = variant.Size?.Label;
            if (variant.Price.HasValue)
            {
                variantSheet.Cell(vRow, 5).Value = variant.Price.Value;
            }
            if (variant.WholesalePrice.HasValue)
            {
                variantSheet.Cell(vRow, 6).Value = variant.WholesalePrice.Value;
            }
            variantSheet.Cell(vRow, 7).Value = variant.StockQuantity;
            variantSheet.Cell(vRow, 8).Value = variant.ImageUrl;
            vRow++;
        }

        variantSheet.Columns(1, VariantExcelHeaders.Length).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"wlm-products-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    // Maps each column header (row 1) to its column number, so rows are read by header name
    // instead of fixed position — a re-ordered or partially-renamed sheet still imports correctly.
    private static Dictionary<string, int> MapHeaders(IXLWorksheet sheet)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerRow = sheet.Row(1);
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            if (!string.IsNullOrEmpty(header))
            {
                map[header] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    // Returns the column number for the first alias that matches a header in the sheet.
    private static int? FindColumn(Dictionary<string, int> headerMap, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (headerMap.TryGetValue(alias, out var col))
            {
                return col;
            }
        }

        return null;
    }

    public IActionResult BulkUpdate()
    {
        return View(new BulkImportResult());
    }

    /// <summary>
    /// Matches rows to products by SKU (column A). A SKU that already exists gets its Price,
    /// Wholesale Price, and Stock Quantity updated (Name/Category are reference-only for those
    /// rows). A SKU that doesn't exist yet is created as a brand-new product using that row's
    /// Name, Category, Price, Wholesale Price, Stock Quantity, and Image URL.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> BulkUpdate(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, _localizer["Choose an Excel (.xlsx) file to upload."]);
            return View(new BulkImportResult());
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".xlsx")
        {
            ModelState.AddModelError(string.Empty, _localizer["Please upload a valid .xlsx file."]);
            return View(new BulkImportResult());
        }

        using var stream = file.OpenReadStream();
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, _localizer["This file couldn't be read as a valid Excel file. It may be corrupted or not a real .xlsx file."]);
            return View(new BulkImportResult());
        }
        using var _ = workbook; // parsed successfully — dispose it when this action returns

        if (!workbook.Worksheets.Contains("Products") && workbook.Worksheets.Count == 0)
        {
            ModelState.AddModelError(string.Empty, _localizer["The Excel file has no sheets to read."]);
            return View(new BulkImportResult());
        }

        var sheet = workbook.Worksheets.First();

        var headers = MapHeaders(sheet);
        var skuCol = FindColumn(headers, "Sku", "SKU", "Product Sku", "رمز المنتج");
        var nameCol = FindColumn(headers, "Name", "Product Name", "الاسم", "اسم المنتج");
        var categoryCol = FindColumn(headers, "Category", "الفئة", "القسم");
        var priceCol = FindColumn(headers, "Price (AED)", "Price", "السعر");
        var wholesaleCol = FindColumn(headers, "Wholesale Price (AED)", "Wholesale Price", "سعر الجملة");
        var stockCol = FindColumn(headers, "Stock Quantity", "Stock", "Qty", "الكمية", "كمية المخزون");
        var imageCol = FindColumn(headers, "Image URL", "Image", "رابط الصورة");

        if (skuCol == null || nameCol == null || categoryCol == null || priceCol == null || stockCol == null)
        {
            ModelState.AddModelError(string.Empty,
                _localizer["Couldn't find required columns (Sku, Name, Category, Price, Stock Quantity). Check the column headers in row 1."]);
            return View(new BulkImportResult());
        }

        var result = new BulkImportResult();

        var products = await _context.Products.ToListAsync();
        var productsBySku = products
            .GroupBy(p => p.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var categoriesByName = (await _context.Categories.ToListAsync())
            .ToDictionary(c => c.Name.Trim(), c => c, StringComparer.OrdinalIgnoreCase);

        var defaultMerchant = await _context.Merchants.OrderBy(m => m.Id).FirstOrDefaultAsync();
        var usedSlugs = new HashSet<string>(products.Select(p => p.Slug), StringComparer.OrdinalIgnoreCase);
        var newProducts = new List<Product>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var sku = row.Cell(skuCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(sku))
            {
                continue;
            }

            if (!TryReadDecimal(row.Cell(priceCol.Value), out var price) || price < 0)
            {
                result.Errors.Add($"Row {rowNum} (SKU {sku}): invalid Price value.");
                continue;
            }

            decimal? wholesale = null;
            if (wholesaleCol != null && !row.Cell(wholesaleCol.Value).IsEmpty())
            {
                if (!TryReadDecimal(row.Cell(wholesaleCol.Value), out var w) || w < 0)
                {
                    result.Errors.Add($"Row {rowNum} (SKU {sku}): invalid Wholesale Price value.");
                    continue;
                }
                wholesale = w;
            }

            if (!TryReadInt(row.Cell(stockCol.Value), out var stock) || stock < 0)
            {
                result.Errors.Add($"Row {rowNum} (SKU {sku}): invalid Stock Quantity value.");
                continue;
            }

            if (productsBySku.TryGetValue(sku, out var product))
            {
                product.Price = Math.Round(price, 2);
                product.WholesalePrice = wholesale.HasValue ? Math.Round(wholesale.Value, 2) : null;
                product.StockQuantity = stock;
                result.UpdatedCount++;
                continue;
            }

            // Unrecognized SKU — create a new product from this row instead of skipping it.
            var name = row.Cell(nameCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Row {rowNum} (SKU {sku}): new SKU with no Name — can't create a product without one.");
                continue;
            }

            var categoryName = row.Cell(categoryCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(categoryName) || !categoriesByName.TryGetValue(categoryName, out var category))
            {
                result.Errors.Add($"Row {rowNum} (SKU {sku}): new SKU with an unrecognized Category '{categoryName}' — check spelling against Admin → Categories.");
                continue;
            }

            if (defaultMerchant == null)
            {
                result.Errors.Add($"Row {rowNum} (SKU {sku}): no merchant account exists to assign this new product to.");
                continue;
            }

            var slug = Slugify(name);
            var dedupeSuffix = 2;
            while (!usedSlugs.Add(slug))
            {
                slug = $"{Slugify(name)}-{dedupeSuffix}";
                dedupeSuffix++;
            }

            var imageUrl = imageCol == null || row.Cell(imageCol.Value).IsEmpty() ? null : row.Cell(imageCol.Value).GetString().Trim();

            var newProduct = new Product
            {
                Name = name,
                Slug = slug,
                Sku = sku,
                CategoryId = category.Id,
                MerchantId = defaultMerchant.Id,
                Price = Math.Round(price, 2),
                WholesalePrice = wholesale.HasValue ? Math.Round(wholesale.Value, 2) : null,
                StockQuantity = stock,
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                CreatedAt = DateTime.UtcNow
            };

            newProducts.Add(newProduct);
            productsBySku[sku] = newProduct; // guards against a duplicate SKU appearing twice in the same sheet
            result.CreatedCount++;
        }

        if (newProducts.Count > 0)
        {
            _context.Products.AddRange(newProducts);
        }

        if (result.UpdatedCount > 0 || result.CreatedCount > 0)
        {
            await _context.SaveChangesAsync();
        }

        // Optional second sheet: per color/size combo pricing and stock. Absent entirely on
        // older exports (or a Products-only re-upload) — that's fine, nothing to do then.
        if (workbook.Worksheets.Contains("Variants"))
        {
            await ImportVariantsSheet(workbook.Worksheet("Variants"), productsBySku, result);
        }

        return View(result);
    }

    // Known launch-catalog color names mapped to their real swatch hex — used when a bulk
    // upload introduces a brand-new color name so it isn't stuck with a meaningless gray
    // swatch. Anything not on this list still gets created, just with a neutral placeholder.
    private static readonly Dictionary<string, string> KnownColorHex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Black"] = "#1c1c1c",
        ["Coyote Tan"] = "#b08d57",
        ["Ranger Green"] = "#4b5320",
        ["Charcoal"] = "#36454f",
        ["Brown"] = "#5b3a29",
        ["Silver"] = "#c0c0c0",
        ["Navy"] = "#1b263b",
        ["Khaki"] = "#c3b091",
        ["Slate Gray"] = "#6c757d",
        ["Olive Drab"] = "#5c5f2e",
        ["Arctic White"] = "#eef1ee"
    };

    /// <summary>
    /// Matches rows to variants by Variant Sku (column B). An existing Variant Sku gets its
    /// Price, Wholesale Price, and Stock Quantity updated. A new Variant Sku is created and
    /// attached to the product named in Product Sku (Parent) (column A) — which must already
    /// exist, including any product just created earlier in the same upload. Color/Size names
    /// that don't exist yet are created on the fly.
    /// </summary>
    private async Task ImportVariantsSheet(IXLWorksheet sheet, Dictionary<string, Product> productsBySku, BulkImportResult result)
    {
        var headers = MapHeaders(sheet);
        var parentSkuCol = FindColumn(headers, "Product Sku (Parent)", "Parent Sku", "SKU الأساسي");
        var variantSkuCol = FindColumn(headers, "Variant Sku", "SKU المتغير");
        var colorCol = FindColumn(headers, "Color", "اللون");
        var sizeCol = FindColumn(headers, "Size", "المقاس");
        var priceCol = FindColumn(headers, "Price (AED)", "Price", "السعر");
        var wholesaleCol = FindColumn(headers, "Wholesale Price (AED)", "Wholesale Price", "سعر الجملة");
        var stockCol = FindColumn(headers, "Stock Quantity", "Stock", "الكمية");
        var imageCol = FindColumn(headers, "Image URL", "Image");

        if (parentSkuCol == null || variantSkuCol == null || stockCol == null)
        {
            result.Errors.Add("Variants sheet: couldn't find required columns (Product Sku (Parent), Variant Sku, Stock Quantity).");
            return;
        }

        var existingVariants = (await _context.ProductVariants.ToListAsync())
            .GroupBy(v => v.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var colorsByName = (await _context.Colors.ToListAsync())
            .ToDictionary(c => c.Name.Trim(), c => c, StringComparer.OrdinalIgnoreCase);
        var sizesByLabel = (await _context.Sizes.ToListAsync())
            .ToDictionary(s => s.Label.Trim(), s => s, StringComparer.OrdinalIgnoreCase);
        var newVariants = new List<ProductVariant>();

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var variantSku = row.Cell(variantSkuCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(variantSku))
            {
                continue;
            }

            if (!TryReadInt(row.Cell(stockCol.Value), out var stock) || stock < 0)
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): invalid Stock Quantity value.");
                continue;
            }

            decimal? price = null;
            if (priceCol != null && !row.Cell(priceCol.Value).IsEmpty())
            {
                if (!TryReadDecimal(row.Cell(priceCol.Value), out var p) || p < 0)
                {
                    result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): invalid Price value.");
                    continue;
                }
                price = Math.Round(p, 2);
            }

            decimal? wholesale = null;
            if (wholesaleCol != null && !row.Cell(wholesaleCol.Value).IsEmpty())
            {
                if (!TryReadDecimal(row.Cell(wholesaleCol.Value), out var w) || w < 0)
                {
                    result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): invalid Wholesale Price value.");
                    continue;
                }
                wholesale = Math.Round(w, 2);
            }

            var imageUrl = imageCol == null || row.Cell(imageCol.Value).IsEmpty() ? null : row.Cell(imageCol.Value).GetString().Trim();

            if (existingVariants.TryGetValue(variantSku, out var existingVariant))
            {
                existingVariant.Price = price;
                existingVariant.WholesalePrice = wholesale;
                existingVariant.StockQuantity = stock;
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    existingVariant.ImageUrl = imageUrl;
                }
                result.VariantsUpdatedCount++;
                continue;
            }

            var parentSku = row.Cell(parentSkuCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(parentSku) || !productsBySku.TryGetValue(parentSku, out var parentProduct))
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): Product Sku (Parent) '{parentSku}' doesn't match any product.");
                continue;
            }

            Color? color = null;
            var colorName = colorCol == null ? string.Empty : row.Cell(colorCol.Value).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(colorName))
            {
                if (!colorsByName.TryGetValue(colorName, out color))
                {
                    color = new Color { Name = colorName, HexCode = KnownColorHex.GetValueOrDefault(colorName, "#808080") };
                    colorsByName[colorName] = color;
                    _context.Colors.Add(color);
                }
            }

            Size? size = null;
            var sizeLabel = sizeCol == null ? string.Empty : row.Cell(sizeCol.Value).GetString().Trim();
            if (!string.IsNullOrWhiteSpace(sizeLabel))
            {
                if (!sizesByLabel.TryGetValue(sizeLabel, out size))
                {
                    size = new Size { Label = sizeLabel, SortOrder = sizesByLabel.Count };
                    sizesByLabel[sizeLabel] = size;
                    _context.Sizes.Add(size);
                }
            }

            var newVariant = new ProductVariant
            {
                Product = parentProduct,
                Color = color,
                Size = size,
                Sku = variantSku,
                Price = price,
                WholesalePrice = wholesale,
                StockQuantity = stock,
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl
            };
            newVariants.Add(newVariant);
            existingVariants[variantSku] = newVariant; // guards against a duplicate SKU appearing twice in the same sheet
            result.VariantsCreatedCount++;
        }

        if (newVariants.Count > 0)
        {
            _context.ProductVariants.AddRange(newVariants);
        }

        if (result.VariantsUpdatedCount > 0 || result.VariantsCreatedCount > 0)
        {
            await _context.SaveChangesAsync();
        }
    }

    private static string Slugify(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }

    private static bool TryReadDecimal(IXLCell cell, out decimal value)
    {
        if (cell.TryGetValue(out double d))
        {
            value = (decimal)d;
            return true;
        }
        return decimal.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadInt(IXLCell cell, out int value)
    {
        if (cell.TryGetValue(out double d))
        {
            value = (int)Math.Round(d);
            return true;
        }
        return int.TryParse(cell.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
