using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
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
    private readonly IOutputCacheStore _outputCacheStore;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer, IOutputCacheStore outputCacheStore, ILogger<ProductsController> logger)
    {
        _context = context;
        _localizer = localizer;
        _outputCacheStore = outputCacheStore;
        _logger = logger;
    }

    // Deliberately unfiltered by IsPublished at the base query — this is the staff-facing view,
    // the only place in the app that lists Draft products at all (every customer-facing listing
    // and the public search filter to Published only). "status" narrows it back down and
    // defaults to "draft", since finding unpublished products (e.g. the Condor import batch
    // stuck in Draft) is this view's main job.
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        var statusFilter = status?.ToLowerInvariant() switch
        {
            "published" => "published",
            "all" => "all",
            _ => "draft"
        };

        var query = _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsQueryable();

        query = statusFilter switch
        {
            "draft" => query.Where(p => !p.IsPublished),
            "published" => query.Where(p => p.IsPublished),
            _ => query
        };

        // Same multi-field match as the customer-facing search (SKU / numeric Id / variant
        // Barcode / Name / Brand / variant Color / Description), minus the IsPublished
        // restriction — that's handled by statusFilter above instead, independently.
        var searchTrimmed = search?.Trim() ?? string.Empty;
        var searchIsNumericId = int.TryParse(searchTrimmed, out var searchNumericId);
        if (!string.IsNullOrWhiteSpace(searchTrimmed))
        {
            query = query.Where(p =>
                EF.Functions.ILike(p.Sku, $"%{searchTrimmed}%") ||
                (searchIsNumericId && p.Id == searchNumericId) ||
                p.Variants.Any(v => v.Barcode != null && EF.Functions.ILike(v.Barcode, $"%{searchTrimmed}%")) ||
                p.Variants.Any(v => EF.Functions.ILike(v.Sku, $"%{searchTrimmed}%")) ||
                EF.Functions.ILike(p.Name, $"%{searchTrimmed}%") ||
                (p.NameAr != null && EF.Functions.ILike(p.NameAr, $"%{searchTrimmed}%")) ||
                (p.Brand != null && EF.Functions.ILike(p.Brand.Name, $"%{searchTrimmed}%")) ||
                p.Variants.Any(v => v.Color != null && EF.Functions.ILike(v.Color.Name, $"%{searchTrimmed}%")) ||
                (p.ShortDescription != null && EF.Functions.ILike(p.ShortDescription, $"%{searchTrimmed}%")) ||
                (p.ShortDescriptionAr != null && EF.Functions.ILike(p.ShortDescriptionAr, $"%{searchTrimmed}%")) ||
                (p.Description != null && EF.Functions.ILike(p.Description, $"%{searchTrimmed}%")));
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
            StatusFilter = statusFilter,
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
        await _outputCacheStore.EvictByTagAsync("products", HttpContext.RequestAborted);
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
        ViewBag.ProductColors = await _context.ProductColors
            .AsNoTracking()
            .Include(pc => pc.Color)
            .Where(pc => pc.ProductId == id)
            .OrderBy(pc => pc.DisplayOrder)
            .ToListAsync();
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
            ViewBag.ProductColors = await _context.ProductColors
                .AsNoTracking()
                .Include(pc => pc.Color)
                .Where(pc => pc.ProductId == id)
                .OrderBy(pc => pc.DisplayOrder)
                .ToListAsync();
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
        existing.VendorSku = product.VendorSku;
        existing.StockQuantity = product.StockQuantity;
        existing.ImageUrl = product.ImageUrl;
        existing.IsFeatured = product.IsFeatured;

        await _context.SaveChangesAsync();
        await _outputCacheStore.EvictByTagAsync("products", HttpContext.RequestAborted);
        TempData["AdminMessage"] = _localizer["Product '{0}' updated.", existing.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    // Separate small form/action from the main product Edit above — Vendor Color Code lives on
    // ProductColor, not Product, and there's no existing per-color admin CRUD to hang this off
    // of (colors are otherwise only ever created via the bulk Product Import). Keeping it as its
    // own POST avoids entangling ProductColor updates with the main product form's model binding.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVendorColorCodes(int id, Dictionary<int, string?> vendorColorCodes)
    {
        var colors = await _context.ProductColors.Where(pc => pc.ProductId == id).ToListAsync();
        if (colors.Count == 0)
        {
            return NotFound();
        }

        foreach (var color in colors)
        {
            if (vendorColorCodes.TryGetValue(color.Id, out var code))
            {
                color.VendorColorCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            }
        }

        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Vendor color codes updated."].Value;
        return RedirectToAction(nameof(Edit), new { id });
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
        await _outputCacheStore.EvictByTagAsync("products", HttpContext.RequestAborted);
        TempData["AdminMessage"] = _localizer["Product '{0}' deleted.", product.Name].Value;

        return RedirectToAction(nameof(Index));
    }

    // Price (AED) is EXCLUDING VAT, Price+VAT (and Wholesale Price+VAT) is INCLUDING VAT — the
    // value actually stored (Product.Price/WholesalePrice are always VAT-inclusive; see
    // TryResolvePriceCells). Both sheets use the identical pair for both Price and Wholesale
    // Price so the same header never means two different things depending on which sheet it's
    // on — see the fix note in TryResolvePriceCells for why that mattered.
    private static readonly string[] ExcelHeaders =
    {
        "Sku", "Name", "Category", "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Wholesale Price+VAT", "Stock Quantity", "Image URL",
        "Name (Arabic)", "Brand", "Subcategory", "Size Group", "Published"
    };
    // Column order otherwise matches the supplier reference format exactly — this is the layout
    // a new supplier's own spreadsheet already comes in, not just our own export/re-import shape.
    // See ImportVariantsSheet for what each column means (Internal Barcode, Size+Length, and the
    // Price (AED)/Price+VAT and Wholesale Price (AED)/Wholesale Price+VAT pairs all have rules of
    // their own).
    private static readonly string[] VariantExcelHeaders =
    {
        "Product Sku (Parent)", "Internal Barcode", "Variant Sku", "Color", "Size", "Length",
        "Price (AED)", "Price+VAT", "Wholesale Price (AED)", "Wholesale Price+VAT", "Stock Quantity", "Image URL"
    };

    /// <summary>Downloads the full catalog as an .xlsx (Products + Variants sheets) — the same file layout <see cref="BulkUpdate(IFormFile?)"/> expects back.</summary>
    public async Task<IActionResult> ExportExcel()
    {
        var products = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Subcategory)
            .Include(p => p.SizeGroup)
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
            // Stored value is VAT-inclusive — both columns are filled on export so either one
            // alone still round-trips through a re-import.
            sheet.Cell(row, 4).Value = Math.Round(product.Price / 1.05m, 2);
            sheet.Cell(row, 5).Value = product.Price;
            if (product.WholesalePrice.HasValue)
            {
                sheet.Cell(row, 6).Value = Math.Round(product.WholesalePrice.Value / 1.05m, 2);
                sheet.Cell(row, 7).Value = product.WholesalePrice.Value;
            }
            sheet.Cell(row, 8).Value = product.StockQuantity;
            sheet.Cell(row, 9).Value = product.ImageUrl;
            sheet.Cell(row, 10).Value = product.NameAr;
            sheet.Cell(row, 11).Value = product.Brand?.Name;
            sheet.Cell(row, 12).Value = product.Subcategory?.Name;
            sheet.Cell(row, 13).Value = product.SizeGroup?.NameEn;
            sheet.Cell(row, 14).Value = product.IsPublished ? "Yes" : "No";
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
            variantSheet.Cell(vRow, 2).Value = variant.Barcode;
            variantSheet.Cell(vRow, 3).Value = variant.Sku;
            variantSheet.Cell(vRow, 4).Value = variant.Color?.Name;
            if (variant.Size != null)
            {
                var (sizePart, lengthPart) = SplitSizeLabel(variant.Size.Label);
                variantSheet.Cell(vRow, 5).Value = sizePart;
                if (lengthPart != null)
                {
                    variantSheet.Cell(vRow, 6).Value = lengthPart;
                }
            }
            if (variant.Price.HasValue)
            {
                // Stored value is VAT-inclusive (see ImportVariantsSheet) — both columns are
                // filled on export so either one alone still round-trips through a re-import.
                variantSheet.Cell(vRow, 7).Value = Math.Round(variant.Price.Value / 1.05m, 2);
                variantSheet.Cell(vRow, 8).Value = variant.Price.Value;
            }
            if (variant.WholesalePrice.HasValue)
            {
                variantSheet.Cell(vRow, 9).Value = Math.Round(variant.WholesalePrice.Value / 1.05m, 2);
                variantSheet.Cell(vRow, 10).Value = variant.WholesalePrice.Value;
            }
            variantSheet.Cell(vRow, 11).Value = variant.StockQuantity;
            variantSheet.Cell(vRow, 12).Value = variant.ImageUrl;
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
    /// Stock Quantity, and any non-blank cell among Wholesale Price / Name (Arabic) / Brand /
    /// Subcategory / Size Group / Published updated — a blank cell always leaves that field
    /// unchanged, never wipes it (Name/Category themselves stay reference-only for those rows,
    /// as before). A SKU that doesn't exist yet is created as a brand-new product from the whole
    /// row — Published defaults to No unless the cell says Yes, and it's assigned to the default
    /// merchant. Brand/Subcategory/Size Group are matched by name to existing records only —
    /// never auto-created — and an unrecognized name fails just that row. Stock Quantity is
    /// overridden after the Variants sheet runs for any product that ends up with variants: it's
    /// always the sum of that product's variants' stock, system-wide, not the cell value here.
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
        var priceExclVatCol = FindColumn(headers, "Price (AED)", "Price", "السعر");
        var priceInclVatCol = FindColumn(headers, "Price+VAT", "Price + VAT", "Price Incl. VAT", "السعر شامل الضريبة");
        var wholesaleExclVatCol = FindColumn(headers, "Wholesale Price (AED)", "Wholesale Price", "سعر الجملة");
        var wholesaleInclVatCol = FindColumn(headers, "Wholesale Price+VAT", "Wholesale Price + VAT", "سعر الجملة شامل الضريبة");
        var stockCol = FindColumn(headers, "Stock Quantity", "Stock", "Qty", "الكمية", "كمية المخزون");
        var imageCol = FindColumn(headers, "Image URL", "Image", "رابط الصورة");

        // All optional — an older export, or a supplier file that never had these columns,
        // still imports fine without them.
        var nameArCol = FindColumn(headers, "Name (Arabic)", "Name Ar", "الاسم بالعربية", "الاسم العربي");
        var brandCol = FindColumn(headers, "Brand", "العلامة التجارية", "الماركة");
        var subcategoryCol = FindColumn(headers, "Subcategory", "الفئة الفرعية", "القسم الفرعي");
        var sizeGroupCol = FindColumn(headers, "Size Group", "مجموعة المقاسات");
        var publishedCol = FindColumn(headers, "Published", "منشور");

        // No incl.-VAT column anywhere in the sheet at all — not just blank on this row — means
        // this is an older export shape, where the excl.-VAT-named column held the VAT-INCLUSIVE
        // price directly (there was no VAT split at all here either). See TryResolvePriceCells,
        // and ImportVariantsSheet's identical flags — Price and Wholesale Price are checked
        // independently in case a hand-edited file only added one pair.
        var isOldPriceFormat = priceInclVatCol == null;
        var isOldWholesaleFormat = wholesaleInclVatCol == null;

        if (skuCol == null || nameCol == null || categoryCol == null || priceExclVatCol == null || stockCol == null)
        {
            ModelState.AddModelError(string.Empty,
                _localizer["Couldn't find required columns (Sku, Name, Category, Price, Stock Quantity). Check the column headers in row 1."]);
            return View(new BulkImportResult());
        }

        var result = new BulkImportResult();

        // Postgres is configured with EnableRetryOnFailure (see Program.cs's ConfigureNpgsql),
        // so _context.Database is backed by a NpgsqlRetryingExecutionStrategy. That strategy
        // refuses a transaction opened with a bare BeginTransactionAsync() outside of its own
        // ExecuteAsync — "does not support user-initiated transactions" — because if a transient
        // failure hits mid-transaction, it has no way to redo a transaction it didn't start.
        // Everything that touches the database for this import (BeginTransaction, both sheets'
        // row loops, SaveChanges, Commit) has to live inside the delegate below instead, so the
        // whole thing is one retriable unit.
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                // A retry re-runs this delegate from the top. Anything left over from a failed
                // attempt — entities the ChangeTracker picked up before the failure, or counts/
                // errors already appended to `result` — has to be cleared here, or a retry
                // double-adds tracked entities and double-counts rows against the same `result`.
                _context.ChangeTracker.Clear();
                result.UpdatedCount = 0;
                result.CreatedCount = 0;
                result.VariantsUpdatedCount = 0;
                result.VariantsCreatedCount = 0;
                result.Errors.Clear();

                // The whole import — both sheets — is one all-or-nothing transaction: either
                // everything below commits together, or (on any error) none of it does. A single
                // SaveChangesAsync at the very end, not one per sheet, is what makes that true.
                await using var transaction = await _context.Database.BeginTransactionAsync();

                var products = await _context.Products.ToListAsync();
                var productsBySku = products
                    .GroupBy(p => p.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                // Grouped-then-first, not a plain ToDictionary: pre-existing duplicate Category
                // names (differing only by case, or a stray one left over from data cleanup) would
                // otherwise throw "An item with the same key has already been added" and take the
                // whole import down with a 500 before a single row is even read.
                var categoriesByName = (await _context.Categories.ToListAsync())
                    .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);

                // Brand, Subcategory, and Size Group are matched by name only — never created by
                // this import (unlike Color/Size on the Variants sheet). Same grouped-then-first
                // defense against pre-existing duplicate names as Categories above.
                var brandsByName = (await _context.Brands.ToListAsync())
                    .GroupBy(b => b.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First(), StringComparer.OrdinalIgnoreCase);
                var sizeGroupsByName = (await _context.SizeGroups.ToListAsync())
                    .GroupBy(sg => sg.NameEn.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderBy(sg => sg.Id).First(), StringComparer.OrdinalIgnoreCase);
                // A Subcategory name only has to be unique within its own Category (two different
                // categories can each have a "Accessories" subcategory), so this is keyed by
                // (CategoryId, lowercased name) rather than name alone. The name is pre-lowercased
                // into the key itself since ValueTuple's default equality is ordinal.
                var subcategoriesByCategoryAndName = (await _context.Subcategories.ToListAsync())
                    .GroupBy(s => (s.CategoryId, Name: s.Name.Trim().ToLowerInvariant()))
                    .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).First());

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

                    // hasValue distinguishes "both Price cells blank" from a resolved value — see
                    // TryResolvePriceCells. Price is required on every Products row (unlike
                    // Wholesale below), so unlike the Variants sheet a missing value is itself an
                    // error here, not "leave unchanged" — matches this sheet's existing behavior.
                    if (!TryResolvePriceCells(row, priceExclVatCol, priceInclVatCol, isOldPriceFormat, "Price (AED)", "Price+VAT", out var hasPriceValue, out var price, out var priceError))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): {priceError}");
                        continue;
                    }
                    if (!hasPriceValue)
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): missing Price (AED) / Price+VAT value.");
                        continue;
                    }

                    // hasWholesaleCell distinguishes "blank — leave unchanged" from "provided —
                    // apply it", which a nullable `wholesale` alone can't: null already means
                    // both "not on this row" and "clear it", and only the latter should ever wipe
                    // an existing value.
                    if (!TryResolvePriceCells(row, wholesaleExclVatCol, wholesaleInclVatCol, isOldWholesaleFormat, "Wholesale Price (AED)", "Wholesale Price+VAT", out var hasWholesaleCell, out var wholesaleValue, out var wholesaleError))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): {wholesaleError}");
                        continue;
                    }
                    decimal? wholesale = hasWholesaleCell ? wholesaleValue : null;

                    if (!TryReadInt(row.Cell(stockCol.Value), out var stock) || stock < 0)
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): invalid Stock Quantity value.");
                        continue;
                    }

                    // Optional classification columns. Blank means "leave unchanged" on an update
                    // row (never wipe a value just because the cell is empty) and "use the
                    // default" on a new-product row.
                    var nameAr = nameArCol == null || row.Cell(nameArCol.Value).IsEmpty() ? null : row.Cell(nameArCol.Value).GetString().Trim();
                    var brandName = brandCol == null || row.Cell(brandCol.Value).IsEmpty() ? null : row.Cell(brandCol.Value).GetString().Trim();
                    var subcategoryName = subcategoryCol == null || row.Cell(subcategoryCol.Value).IsEmpty() ? null : row.Cell(subcategoryCol.Value).GetString().Trim();
                    var sizeGroupName = sizeGroupCol == null || row.Cell(sizeGroupCol.Value).IsEmpty() ? null : row.Cell(sizeGroupCol.Value).GetString().Trim();
                    var publishedText = publishedCol == null || row.Cell(publishedCol.Value).IsEmpty() ? null : row.Cell(publishedCol.Value).GetString().Trim();

                    // Brand and Size Group aren't scoped to a Category, so they resolve the same
                    // way regardless of whether this row turns out to be an update or a create.
                    // Never auto-created — an unrecognized name is a row error, not a new record.
                    Brand? brand = null;
                    if (brandName != null && !brandsByName.TryGetValue(brandName, out brand))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): unrecognized Brand '{brandName}' — check spelling against Admin → Brands.");
                        continue;
                    }

                    SizeGroup? sizeGroup = null;
                    if (sizeGroupName != null && !sizeGroupsByName.TryGetValue(sizeGroupName, out sizeGroup))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): unrecognized Size Group '{sizeGroupName}' — check spelling against Admin → Size Groups.");
                        continue;
                    }

                    if (productsBySku.TryGetValue(sku, out var product))
                    {
                        // Subcategory is validated against the product's CURRENT category — this
                        // row's Category cell is reference-only for an update (as Name already
                        // is), so changing it here isn't supported and shouldn't silently change
                        // which category the Subcategory has to belong to either.
                        Subcategory? subcategory = null;
                        if (subcategoryName != null &&
                            !subcategoriesByCategoryAndName.TryGetValue((product.CategoryId, subcategoryName.ToLowerInvariant()), out subcategory))
                        {
                            result.Errors.Add($"Products row {rowNum} (SKU {sku}): unrecognized Subcategory '{subcategoryName}' for this product's current category — check spelling, and that it belongs to the right category.");
                            continue;
                        }

                        product.Price = Math.Round(price, 2);
                        if (hasWholesaleCell)
                        {
                            product.WholesalePrice = Math.Round(wholesale!.Value, 2);
                        }
                        product.StockQuantity = stock; // overridden below for any product that ends up with variants
                        if (nameAr != null) product.NameAr = nameAr;
                        if (brandName != null) product.BrandId = brand!.Id;
                        if (subcategoryName != null) product.SubcategoryId = subcategory!.Id;
                        if (sizeGroupName != null) product.SizeGroupId = sizeGroup!.Id;
                        if (publishedText != null && TryReadYesNo(publishedText, out var isPublishedUpdate))
                        {
                            product.IsPublished = isPublishedUpdate;
                        }
                        result.UpdatedCount++;
                        continue;
                    }

                    // Unrecognized SKU — create a new product from this row instead of skipping it.
                    var name = row.Cell(nameCol.Value).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): new SKU with no Name — can't create a product without one.");
                        continue;
                    }

                    var categoryName = row.Cell(categoryCol.Value).GetString().Trim();
                    if (string.IsNullOrWhiteSpace(categoryName) || !categoriesByName.TryGetValue(categoryName, out var category))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): new SKU with an unrecognized Category '{categoryName}' — check spelling against Admin → Categories.");
                        continue;
                    }

                    Subcategory? subcategoryForCreate = null;
                    if (subcategoryName != null &&
                        !subcategoriesByCategoryAndName.TryGetValue((category.Id, subcategoryName.ToLowerInvariant()), out subcategoryForCreate))
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): unrecognized Subcategory '{subcategoryName}' for Category '{categoryName}' — check spelling, and that it belongs to the right category.");
                        continue;
                    }

                    if (defaultMerchant == null)
                    {
                        result.Errors.Add($"Products row {rowNum} (SKU {sku}): no merchant account exists to assign this new product to.");
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

                    // Unpublished by default — Published = Yes is opt-in, not opt-out, so a
                    // blank/unrecognized cell (or an explicit "No") both land here as false.
                    var isPublishedNew = publishedText != null && TryReadYesNo(publishedText, out var publishedValueNew) && publishedValueNew;

                    var newProduct = new Product
                    {
                        Name = name,
                        NameAr = nameAr,
                        Slug = slug,
                        Sku = sku,
                        CategoryId = category.Id,
                        SubcategoryId = subcategoryForCreate?.Id,
                        BrandId = brand?.Id,
                        SizeGroupId = sizeGroup?.Id,
                        MerchantId = defaultMerchant.Id,
                        Price = Math.Round(price, 2),
                        WholesalePrice = hasWholesaleCell ? Math.Round(wholesale!.Value, 2) : null,
                        StockQuantity = stock,
                        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                        IsPublished = isPublishedNew,
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

                // Optional second sheet: per color/size combo pricing and stock. Absent entirely
                // on older exports (or a Products-only re-upload) — that's fine, nothing to do.
                if (workbook.Worksheets.Contains("Variants"))
                {
                    await ImportVariantsSheet(workbook.Worksheet("Variants"), productsBySku, result);
                }

                // Any product that has variants always has its Stock Quantity set to the sum of
                // its variants' stock — the Products sheet's Stock Quantity cell is ignored for
                // that product (set above, then overridden here). This runs from the
                // ChangeTracker rather than a fresh DB query, deliberately: a brand-new product
                // or variant created earlier in this same attempt has no database row yet to
                // query, but is already tracked. ChangeTracker.Entries<ProductVariant>() also
                // still includes every pre-existing variant this file never even mentioned
                // (loaded, untouched, and still tracked as Unchanged) — not just the ones this
                // upload touched — which is what makes the sum correct for a partial re-upload.
                var productsById = products.ToDictionary(p => p.Id);
                var variantStockByProduct = new Dictionary<Product, int>();
                foreach (var entry in _context.ChangeTracker.Entries<ProductVariant>())
                {
                    var trackedVariant = entry.Entity;
                    // A variant just created in this attempt has its Product navigation set
                    // directly (see ImportVariantsSheet) but no real ProductId yet — that FK is
                    // only resolved from the navigation property when SaveChanges runs. A
                    // pre-existing variant is the opposite: loaded straight from the database
                    // with a real ProductId, but without .Product (never Include()d).
                    var owningProduct = trackedVariant.Product
                        ?? (productsById.TryGetValue(trackedVariant.ProductId, out var existingOwner) ? existingOwner : null);
                    if (owningProduct == null)
                    {
                        continue;
                    }

                    variantStockByProduct[owningProduct] = variantStockByProduct.GetValueOrDefault(owningProduct) + trackedVariant.StockQuantity;
                }

                foreach (var (productWithVariants, totalVariantStock) in variantStockByProduct)
                {
                    productWithVariants.StockQuantity = totalVariantStock;
                }

                if (result.UpdatedCount > 0 || result.CreatedCount > 0 || result.VariantsUpdatedCount > 0 || result.VariantsCreatedCount > 0)
                {
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            });
        }
        catch (Exception ex)
        {
            // No RollbackAsync here: the transaction lives inside the lambda above and is opened
            // with `await using`, so an exception unwinding out of that scope already disposes
            // (and thereby rolls back) whatever this attempt had done, before the execution
            // strategy either retries the whole delegate or — once retries are exhausted, or the
            // failure isn't one it considers transient — rethrows out here.
            _logger.LogError(ex, "Bulk Update import failed and was rolled back; no changes were saved.");

            return View(new BulkImportResult
            {
                Errors = result.Errors,
                FatalError = _localizer["The import failed and NO changes were saved (the whole file is rolled back together). Reason: {0}", ex.Message].Value
            });
        }

        // Cache eviction happens once the transaction has definitively committed — it's not part
        // of the retriable DB unit above (retrying it wouldn't help, and it isn't the reason an
        // import would fail), and a failure here shouldn't turn an import that DID save into a
        // "nothing was saved" error page for the admin.
        if (result.UpdatedCount > 0 || result.CreatedCount > 0 || result.VariantsUpdatedCount > 0 || result.VariantsCreatedCount > 0)
        {
            try
            {
                await _outputCacheStore.EvictByTagAsync("products", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk Update import saved successfully, but evicting the 'products' output cache tag failed; cached pages may be stale until the next natural eviction.");
            }
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
    /// Stock Quantity, and any non-blank cell among Price / Wholesale Price / Internal Barcode,
    /// updated — a blank cell leaves that field unchanged, never wipes it. A new Variant Sku is
    /// created and attached to the product named in Product Sku (Parent) (column A) — which must
    /// already exist, including any product just created earlier in the same upload. Color/Size
    /// names that don't exist yet are created on the fly (Size is combined from the Size + Length
    /// columns — see CombineSizeAndLength). Internal Barcode, when provided, must be unique
    /// across every variant — a duplicate within this same file, or one that already belongs to
    /// a different variant in the database, fails just that row. Price (AED)/Price+VAT and
    /// Wholesale Price (AED)/Wholesale Price+VAT are each resolved to the single VAT-inclusive
    /// value actually stored — see TryResolvePriceCells.
    /// </summary>
    private async Task ImportVariantsSheet(IXLWorksheet sheet, Dictionary<string, Product> productsBySku, BulkImportResult result)
    {
        var headers = MapHeaders(sheet);
        var parentSkuCol = FindColumn(headers, "Product Sku (Parent)", "Parent Sku", "SKU الأساسي");
        var variantSkuCol = FindColumn(headers, "Variant Sku", "SKU المتغير");
        var colorCol = FindColumn(headers, "Color", "اللون");
        var sizeCol = FindColumn(headers, "Size", "المقاس");
        var lengthCol = FindColumn(headers, "Length", "الطول");
        var priceExclVatCol = FindColumn(headers, "Price (AED)", "Price", "السعر");
        var priceInclVatCol = FindColumn(headers, "Price+VAT", "Price + VAT", "Price Incl. VAT", "السعر شامل الضريبة");
        var wholesaleExclVatCol = FindColumn(headers, "Wholesale Price (AED)", "Wholesale Price", "سعر الجملة");
        var wholesaleInclVatCol = FindColumn(headers, "Wholesale Price+VAT", "Wholesale Price + VAT", "سعر الجملة شامل الضريبة");
        var stockCol = FindColumn(headers, "Stock Quantity", "Stock", "الكمية");
        var imageCol = FindColumn(headers, "Image URL", "Image");
        var barcodeCol = FindColumn(headers, "Internal Barcode", "Barcode", "الباركود الداخلي", "الباركود");

        // No incl.-VAT column anywhere in the sheet at all — not just blank on this row — means
        // this is an older export shape, where the excl.-VAT-named column held the VAT-INCLUSIVE
        // price directly (there was no VAT split at all). That old meaning has to be preserved
        // exactly, never reinterpreted as the new excl.-VAT column of the same name — see
        // TryResolvePriceCells, which branches on this flag before reading either cell. Price and
        // Wholesale Price are checked independently in case a hand-edited file only added one pair.
        var isOldPriceFormat = priceInclVatCol == null;
        var isOldWholesaleFormat = wholesaleInclVatCol == null;

        if (parentSkuCol == null || variantSkuCol == null || stockCol == null)
        {
            result.Errors.Add("Variants sheet: couldn't find required columns (Product Sku (Parent), Variant Sku, Stock Quantity).");
            return;
        }

        var existingVariants = (await _context.ProductVariants.ToListAsync())
            .GroupBy(v => v.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // Grouped-then-first (lowest Id wins, deterministically), not a plain ToDictionary:
        // Color.Name and Size.Label have no uniqueness constraint in the database (only their
        // Code does — see ApplicationDbContext), so pre-existing duplicates — e.g. two colors
        // whose names collide only by case, or two sizes that differ only by "x" vs "×" — are
        // entirely possible and, before this fix, threw "An item with the same key has already
        // been added" here and took the whole import down with a generic 500 before a single
        // variant row was read. Keying Sizes by NormalizeSizeKey (not the raw label) means an
        // incoming "30X32" or "30 × 32" also lands on that same existing row below, instead of
        // minting a near-duplicate size that differs only by separator or case.
        var colorsByName = (await _context.Colors.ToListAsync())
            .GroupBy(c => c.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.Id).First(), StringComparer.OrdinalIgnoreCase);
        var sizesByKey = (await _context.Sizes.ToListAsync())
            .GroupBy(s => NormalizeSizeKey(s.Label))
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Id).First());

        // Barcode is unique across ALL variants (enforced by a DB index too — see
        // ApplicationDbContext — this dictionary just turns a violation into a friendly per-row
        // error instead of a whole-transaction rollback). Grouped-then-first for the same
        // pre-existing-duplicate defense as Colors/Sizes above. Re-registering the owner after
        // every accepted row (below) is what also catches the SAME barcode appearing twice
        // within this file, not just a collision against the database.
        var variantsByBarcode = existingVariants.Values
            .Where(v => !string.IsNullOrWhiteSpace(v.Barcode))
            .GroupBy(v => v.Barcode!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id).First(), StringComparer.OrdinalIgnoreCase);

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

            // hasValue for either distinguishes "both cells blank — leave the existing value
            // alone on an update, don't wipe it" from a resolved value — see TryResolvePriceCells.
            if (!TryResolvePriceCells(row, priceExclVatCol, priceInclVatCol, isOldPriceFormat, "Price (AED)", "Price+VAT", out var hasPriceCell, out var priceValue, out var priceError))
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): {priceError}");
                continue;
            }
            decimal? price = hasPriceCell ? priceValue : null;

            if (!TryResolvePriceCells(row, wholesaleExclVatCol, wholesaleInclVatCol, isOldWholesaleFormat, "Wholesale Price (AED)", "Wholesale Price+VAT", out var hasWholesaleCell, out var wholesaleValue, out var wholesaleError))
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): {wholesaleError}");
                continue;
            }
            decimal? wholesale = hasWholesaleCell ? wholesaleValue : null;

            var imageUrl = imageCol == null || row.Cell(imageCol.Value).IsEmpty() ? null : row.Cell(imageCol.Value).GetString().Trim();
            var barcode = barcodeCol == null || row.Cell(barcodeCol.Value).IsEmpty() ? null : row.Cell(barcodeCol.Value).GetString().Trim();
            if (string.IsNullOrWhiteSpace(barcode))
            {
                barcode = null;
            }

            if (existingVariants.TryGetValue(variantSku, out var existingVariant))
            {
                if (barcode != null)
                {
                    if (variantsByBarcode.TryGetValue(barcode, out var barcodeOwner) && barcodeOwner != existingVariant)
                    {
                        result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): Barcode '{barcode}' already belongs to variant '{barcodeOwner.Sku}'.");
                        continue;
                    }
                    existingVariant.Barcode = barcode;
                    variantsByBarcode[barcode] = existingVariant;
                }
                // else: blank Barcode cell — leave the existing value unchanged.

                if (hasPriceCell)
                {
                    existingVariant.Price = price;
                }
                if (hasWholesaleCell)
                {
                    existingVariant.WholesalePrice = wholesale;
                }
                existingVariant.StockQuantity = stock;
                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    existingVariant.ImageUrl = imageUrl;
                }
                result.VariantsUpdatedCount++;
                continue;
            }

            if (barcode != null && variantsByBarcode.TryGetValue(barcode, out var conflictingVariant))
            {
                result.Errors.Add($"Variants row {rowNum} (SKU {variantSku}): Barcode '{barcode}' already belongs to variant '{conflictingVariant.Sku}'.");
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
            var sizeText = sizeCol == null || row.Cell(sizeCol.Value).IsEmpty() ? null : row.Cell(sizeCol.Value).GetString().Trim();
            var lengthText = lengthCol == null || row.Cell(lengthCol.Value).IsEmpty() ? null : row.Cell(lengthCol.Value).GetString().Trim();
            var sizeLabel = string.IsNullOrWhiteSpace(sizeText) ? null : CombineSizeAndLength(sizeText, string.IsNullOrWhiteSpace(lengthText) ? null : lengthText);
            if (sizeLabel != null)
            {
                var sizeKey = NormalizeSizeKey(sizeLabel);
                if (!sizesByKey.TryGetValue(sizeKey, out size))
                {
                    size = new Size { Label = sizeLabel, SortOrder = sizesByKey.Count };
                    sizesByKey[sizeKey] = size;
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
                ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
                Barcode = barcode
            };
            newVariants.Add(newVariant);
            existingVariants[variantSku] = newVariant; // guards against a duplicate SKU appearing twice in the same sheet
            if (barcode != null)
            {
                variantsByBarcode[barcode] = newVariant; // guards against the same barcode appearing twice in the same sheet
            }
            result.VariantsCreatedCount++;
        }

        if (newVariants.Count > 0)
        {
            _context.ProductVariants.AddRange(newVariants);
        }

        // No SaveChangesAsync here — the caller (BulkUpdate) saves both sheets together in one
        // transaction, so this sheet's changes are committed (or rolled back) atomically with
        // the Products sheet's.
    }

    // Normalizes a size label for matching against existing Sizes: a separator ("x", "X", or
    // "×", with or without surrounding spaces) between two numbers collapses to one canonical
    // form, and the whole label is case-folded — so "30x32", "30X32", and "30 × 32" all resolve
    // to the SAME key and match the SAME existing Size row, instead of each becoming its own
    // near-duplicate size that differs only by punctuation or letter case. Only text between
    // digits is touched, so letter-only labels like "S" or "XL" are unaffected beyond casing.
    private static readonly System.Text.RegularExpressions.Regex SizeSeparatorPattern = new(
        @"(?<=\d)\s*[x×]\s*(?=\d)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string NormalizeSizeKey(string label) =>
        SizeSeparatorPattern.Replace(label.Trim(), "x").ToLowerInvariant();

    // The Variants sheet's Size/Length columns are the split-apart form of a stored Size label:
    // waist/inseam-style sizes ("30x32") are two numbers, everything else (letter sizes, a bare
    // shoe size, "One Size") is just one. Combine (import) joins Size+Length back into the same
    // "NxN" shape NormalizeSizeKey already understands, so a combined "28"+"30" matches (and
    // never duplicates) an existing "28x30" Size exactly like a single-column "28x30" always did.
    private static string CombineSizeAndLength(string size, string? length) =>
        length == null ? size : $"{size}x{length}";

    // Split (export) is the inverse: only a label that IS exactly "<number>x<number>" (whole
    // string, either separator, any case) splits into two columns — a letter size or anything
    // else that doesn't fit that shape exports as Size alone, Length blank, so it reads back
    // through Combine unchanged.
    private static readonly System.Text.RegularExpressions.Regex SizeLengthSplitPattern = new(
        @"^(\d+(?:\.\d+)?)\s*[x×]\s*(\d+(?:\.\d+)?)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static (string Size, string? Length) SplitSizeLabel(string label)
    {
        var match = SizeLengthSplitPattern.Match(label.Trim());
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (label, null);
    }

    // Resolves an excl.-VAT / incl.-VAT column pair (Price (AED)/Price+VAT, or Wholesale Price
    // (AED)/Wholesale Price+VAT — on either the Products or Variants sheet, all four call sites
    // share this one function) into the single VAT-inclusive value actually stored
    // (Product/ProductVariant .Price and .WholesalePrice are always VAT-inclusive — the site
    // stores prices INCLUDING 5% VAT). `hasValue` distinguishes "both cells blank — nothing to
    // apply, leave any existing value unchanged" from a resolved `value`; a `false` return means
    // the row itself is invalid — the caller adds `error` to the results and skips the whole row,
    // same as any other bad cell. exclLabel/inclLabel are just for error messages, so a Wholesale
    // Price problem doesn't get reported as a plain "Price" one.
    //
    // Both sheets used to disagree about what a bare "Price (AED)" column (no pair) meant — excl.
    // VAT on the Variants sheet, incl. VAT on the Products sheet — which was genuinely dangerous:
    // a new product's Price (AED) cell, filled in with the excl.-VAT figure out of habit from the
    // Variants sheet, would have silently become the site's stored (and displayed/charged) price.
    // They now always mean the same thing on both sheets. isOldFormat (no incl.-VAT column
    // anywhere in the sheet, not just blank on this row) exists so a file from before that pairing
    // existed still imports correctly: back then the single column WAS the VAT-inclusive price
    // (there was no split at all), so it's read as-is here — never multiplied by 1.05, which would
    // silently double-apply VAT to a value that already had it.
    private static bool TryResolvePriceCells(
        IXLRow row, int? exclVatCol, int? inclVatCol, bool isOldFormat, string exclLabel, string inclLabel,
        out bool hasValue, out decimal value, out string? error)
    {
        hasValue = false;
        value = 0;
        error = null;

        if (isOldFormat)
        {
            if (exclVatCol == null || row.Cell(exclVatCol.Value).IsEmpty())
            {
                return true;
            }
            if (!TryReadDecimal(row.Cell(exclVatCol.Value), out var oldPrice) || oldPrice < 0)
            {
                error = $"invalid {exclLabel} value.";
                return false;
            }
            hasValue = true;
            value = Math.Round(oldPrice, 2);
            return true;
        }

        var hasExcl = exclVatCol != null && !row.Cell(exclVatCol.Value).IsEmpty();
        var hasIncl = inclVatCol != null && !row.Cell(inclVatCol.Value).IsEmpty();
        if (!hasExcl && !hasIncl)
        {
            return true;
        }

        decimal excl = 0, incl = 0;
        if (hasExcl && (!TryReadDecimal(row.Cell(exclVatCol!.Value), out excl) || excl < 0))
        {
            error = $"invalid {exclLabel} value.";
            return false;
        }
        if (hasIncl && (!TryReadDecimal(row.Cell(inclVatCol!.Value), out incl) || incl < 0))
        {
            error = $"invalid {inclLabel} value.";
            return false;
        }

        if (hasExcl && hasIncl)
        {
            var computed = Math.Round(excl * 1.05m, 2);
            if (Math.Abs(computed - incl) > 0.01m)
            {
                error = $"{exclLabel} {excl} × 1.05 = {computed}, which doesn't match {inclLabel} {incl} (must agree within 0.01).";
                return false;
            }
            hasValue = true;
            value = Math.Round(incl, 2);
            return true;
        }

        if (hasExcl)
        {
            hasValue = true;
            value = Math.Round(excl * 1.05m, 2);
            return true;
        }

        hasValue = true;
        value = Math.Round(incl, 2);
        return true;
    }

    // Tolerates "Yes"/"No", "True"/"False", and "1"/"0" — anything else (including a typo) is
    // unrecognized, not an error: the caller treats an unrecognized Published cell the same as a
    // blank one (leave unchanged on an update, default to unpublished on a new product) rather
    // than failing the whole row over a formatting slip in an optional column.
    private static bool TryReadYesNo(string text, out bool value)
    {
        if (text.Equals("Yes", StringComparison.OrdinalIgnoreCase) || text.Equals("True", StringComparison.OrdinalIgnoreCase) || text == "1")
        {
            value = true;
            return true;
        }
        if (text.Equals("No", StringComparison.OrdinalIgnoreCase) || text.Equals("False", StringComparison.OrdinalIgnoreCase) || text == "0")
        {
            value = false;
            return true;
        }
        value = false;
        return false;
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
