using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;
using static WorldLinkMaster.Web.Areas.Admin.Controllers.ExcelImportHelpers;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

/// <summary>
/// Imports WLM_02_Master_Data.xlsx-style workbooks (Brands, Categories, Colors, Size Groups,
/// Sizes, Attribute Dictionary sheets) per WLM_01_Developer_Specification.xlsx. Run this
/// before the Product Importer, since products reference these codes.
/// </summary>
public class MasterDataController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<MasterDataController> _logger;
    private readonly IStorefrontCacheService _storefrontCache;

    public MasterDataController(ApplicationDbContext context, ILogger<MasterDataController> logger, IStorefrontCacheService storefrontCache)
    {
        _context = context;
        _logger = logger;
        _storefrontCache = storefrontCache;
    }

    public IActionResult Index()
    {
        return View(new MasterDataImportResult());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        var result = new MasterDataImportResult();

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

        // Per-row problems (an unrecognized code, a value that's too long for its column, a bad
        // Action) are validated before ever reaching the database and always land in
        // result.Errors — that row is skipped, nothing else is affected. This try/catch is the
        // backstop for anything NOT caught by that per-row validation (an unexpected constraint
        // violation, a transient connectivity failure, ClosedXML choking on a malformed cell,
        // etc.): every sheet below writes to a shared production database, so any of them can
        // throw, and an uncaught exception here previously propagated all the way out of the
        // action to ASP.NET Core's generic "An error occurred while processing your request" page
        // instead of ever reaching this view.
        try
        {
            if (workbook.Worksheets.Contains("Brands"))
            {
                await ImportBrandsAsync(workbook.Worksheet("Brands"), result);
            }

            if (workbook.Worksheets.Contains("Categories"))
            {
                await ImportCategoriesAsync(workbook.Worksheet("Categories"), result);
            }

            if (workbook.Worksheets.Contains("Colors"))
            {
                await ImportColorsAsync(workbook.Worksheet("Colors"), result);
            }

            if (workbook.Worksheets.Contains("Size Groups"))
            {
                await ImportSizeGroupsAsync(workbook.Worksheet("Size Groups"), result);
            }

            if (workbook.Worksheets.Contains("Sizes"))
            {
                await ImportSizesAsync(workbook.Worksheet("Sizes"), result);
            }

            if (workbook.Worksheets.Contains("Attribute Dictionary"))
            {
                await ImportAttributeDictionaryAsync(workbook.Worksheet("Attribute Dictionary"), result);
            }
        }
        catch (Exception ex)
        {
            // Each sheet above calls SaveChangesAsync independently (not one shared transaction),
            // so whichever sheets already succeeded before this exception stay committed — unlike
            // Bulk Update (ProductsController.BulkUpdate), this import was never all-or-nothing.
            // The counts already recorded in `result` before the failure reflect that.
            _logger.LogError(ex, "Master Data import failed unexpectedly partway through.");
            result.FatalError = $"The import stopped after an unexpected error: {ex.Message}. Any sheets processed before this point were still saved.";
            // Sheets processed before the failure were still saved (see the comment above), so the
            // menu/listing cache could already be stale even on this error path.
            _storefrontCache.Invalidate();
            return View("Index", result);
        }

        _storefrontCache.Invalidate();
        return View("Index", result);
    }

    // An Action of ADD or UPDATE both mean "upsert this row by its Code" here — there's no
    // meaningful difference for a code-keyed upsert. Anything else (blank, DELETE, a typo)
    // is left unprocessed and called out so it isn't silently dropped.
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

    // Checks every (value, maxLength, fieldName) tuple and appends one error per field that's too
    // long — a hand-typed cell overflowing its column (e.g. a long free-text "Unit / Type") is a
    // data-entry mistake, not a crash: see ExcelImportHelpers.ValidateLength for why this can't be
    // left to the database to catch. Returns false (and the row must be skipped) if any field failed.
    private static bool ValidateRowLengths(List<string> errors, string sheetName, int rowNum, params (string? Value, int MaxLength, string FieldName)[] fields)
    {
        var ok = true;
        foreach (var (value, maxLength, fieldName) in fields)
        {
            var error = ValidateLength(value, maxLength, fieldName);
            if (error != null)
            {
                errors.Add($"{sheetName} row {rowNum}: {error}");
                ok = false;
            }
        }
        return ok;
    }

    private async Task ImportBrandsAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Brand Code");
        var nameCol = FindColumn(headers, "Brand Name EN");
        var nameArCol = FindColumn(headers, "Brand Name AR");
        var websiteCol = FindColumn(headers, "Website");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || nameCol == null)
        {
            result.Errors.Add("Brands sheet: couldn't find required columns (Brand Code, Brand Name EN).");
            return;
        }

        var existing = await _context.Brands.Where(b => b.Code != null).ToDictionaryAsync(b => b.Code!, StringComparer.OrdinalIgnoreCase);
        // Brands seeded before this importer existed have no Code — match those by Name so a
        // re-import enriches them instead of creating a parallel duplicate (see incident: the
        // first Master Data import created 4 duplicate brands, 9 duplicate colors, and 5
        // duplicate categories this way).
        var existingByName = await _context.Brands.Where(b => b.Code == null).ToDictionaryAsync(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var usedSlugs = new HashSet<string>((await _context.Brands.Select(b => b.Slug).ToListAsync()), StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Brands", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Brands row {rowNum}: Brand Code and Brand Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            var website = GetString(sheet, rowNum, websiteCol);
            if (!ValidateRowLengths(result.Errors, "Brands", rowNum,
                    (code, 20, "Brand Code"), (name, 80, "Brand Name EN"), (nameAr, 80, "Brand Name AR"), (website, 300, "Website")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Brands row {rowNum}: duplicate Brand Code '{code}' in this sheet.");
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);

            if (existing.TryGetValue(code, out var brand))
            {
                brand.Name = name;
                brand.NameAr = nameAr;
                brand.Website = website;
                brand.Active = active;
                result.BrandsUpdated++;
            }
            else if (existingByName.TryGetValue(name, out brand))
            {
                brand.Code = code;
                brand.NameAr = nameAr;
                brand.Website = website;
                brand.Active = active;
                existing[code] = brand;
                existingByName.Remove(name);
                result.BrandsUpdated++;
            }
            else
            {
                brand = new Brand
                {
                    Code = code,
                    Name = name,
                    NameAr = nameAr,
                    Slug = UniqueSlug(name, usedSlugs),
                    Website = website,
                    Active = active
                };
                _context.Brands.Add(brand);
                existing[code] = brand;
                result.BrandsCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportCategoriesAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Category Code");
        var parentCol = FindColumn(headers, "Parent Category Code");
        var nameCol = FindColumn(headers, "Name EN");
        var nameArCol = FindColumn(headers, "Name AR");
        var orderCol = FindColumn(headers, "Display Order");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || nameCol == null)
        {
            result.Errors.Add("Categories sheet: couldn't find required columns (Category Code, Name EN).");
            return;
        }

        var existingCategories = await _context.Categories.Where(c => c.Code != null).ToDictionaryAsync(c => c.Code!, StringComparer.OrdinalIgnoreCase);
        var existingSubcategories = await _context.Subcategories.Where(s => s.Code != null).ToDictionaryAsync(s => s.Code!, StringComparer.OrdinalIgnoreCase);
        // Categories/subcategories seeded before this importer existed have no Code — match
        // those by Name so a re-import enriches them instead of creating a parallel duplicate.
        // (Doesn't help when the spec renames a category outright, e.g. "Bags" vs. the site's
        // "Bags & Packs" — those still need a one-time manual merge, same as this incident.)
        var existingCategoriesByName = await _context.Categories.Where(c => c.Code == null).ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var existingSubcategoriesByName = await _context.Subcategories.Where(s => s.Code == null).ToDictionaryAsync(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var categorySlugs = new HashSet<string>((await _context.Categories.Select(c => c.Slug).ToListAsync()), StringComparer.OrdinalIgnoreCase);
        var subcategorySlugs = new HashSet<string>((await _context.Subcategories.Select(s => s.Slug).ToListAsync()), StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rows = sheet.RowsUsed().Skip(1).ToList();

        // Pass 1: top-level categories (blank Parent Category Code) — subcategories in pass 2
        // may reference a category created in this same pass.
        foreach (var row in rows)
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Categories", result.Errors)) continue;

            var parentCode = GetString(sheet, rowNum, parentCol);
            if (parentCode != null) continue; // handled in pass 2

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Categories row {rowNum}: Category Code and Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            if (!ValidateRowLengths(result.Errors, "Categories", rowNum,
                    (code, 20, "Category Code"), (name, 100, "Name EN"), (nameAr, 100, "Name AR")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Categories row {rowNum}: duplicate Category Code '{code}' in this sheet.");
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);

            if (existingCategories.TryGetValue(code, out var category))
            {
                category.Name = name;
                category.NameAr = nameAr;
                category.DisplayOrder = displayOrder;
                category.Active = active;
                result.CategoriesUpdated++;
            }
            else if (existingCategoriesByName.TryGetValue(name, out category))
            {
                category.Code = code;
                category.NameAr = nameAr;
                category.DisplayOrder = displayOrder;
                category.Active = active;
                existingCategories[code] = category;
                existingCategoriesByName.Remove(name);
                result.CategoriesUpdated++;
            }
            else
            {
                category = new Category
                {
                    Code = code,
                    Name = name,
                    NameAr = nameAr,
                    Slug = UniqueSlug(name, categorySlugs),
                    DisplayOrder = displayOrder,
                    Active = active
                };
                _context.Categories.Add(category);
                existingCategories[code] = category;
                result.CategoriesCreated++;
            }
        }

        await _context.SaveChangesAsync();

        // Pass 2: subcategories (non-blank Parent Category Code).
        foreach (var row in rows)
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Categories", result.Errors)) continue;

            var parentCode = GetString(sheet, rowNum, parentCol);
            if (parentCode == null) continue; // handled in pass 1

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Categories row {rowNum}: Category Code and Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            if (!ValidateRowLengths(result.Errors, "Categories", rowNum,
                    (code, 20, "Category Code"), (name, 150, "Name EN"), (nameAr, 150, "Name AR")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Categories row {rowNum}: duplicate Category Code '{code}' in this sheet.");
                continue;
            }
            if (!existingCategories.TryGetValue(parentCode, out var parentCategory))
            {
                var suggestion = FindClosestMatch(parentCode, existingCategories.Keys);
                result.Errors.Add(WithSuggestion($"Categories row {rowNum}: Parent Category Code '{parentCode}' doesn't match any top-level category (either in this file or already saved). Note: only 2 levels deep are supported — a subcategory can't itself be a parent.", suggestion));
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);

            if (existingSubcategories.TryGetValue(code, out var subcategory))
            {
                subcategory.Name = name;
                subcategory.NameAr = nameAr;
                subcategory.CategoryId = parentCategory.Id;
                subcategory.DisplayOrder = displayOrder;
                subcategory.Active = active;
                result.SubcategoriesUpdated++;
            }
            else if (existingSubcategoriesByName.TryGetValue(name, out subcategory))
            {
                subcategory.Code = code;
                subcategory.NameAr = nameAr;
                subcategory.CategoryId = parentCategory.Id;
                subcategory.DisplayOrder = displayOrder;
                subcategory.Active = active;
                existingSubcategories[code] = subcategory;
                existingSubcategoriesByName.Remove(name);
                result.SubcategoriesUpdated++;
            }
            else
            {
                subcategory = new Subcategory
                {
                    Code = code,
                    Name = name,
                    NameAr = nameAr,
                    Slug = UniqueSlug(name, subcategorySlugs),
                    CategoryId = parentCategory.Id,
                    DisplayOrder = displayOrder,
                    Active = active
                };
                _context.Subcategories.Add(subcategory);
                existingSubcategories[code] = subcategory;
                result.SubcategoriesCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportColorsAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Color Code");
        var nameCol = FindColumn(headers, "Name EN");
        var nameArCol = FindColumn(headers, "Name AR");
        var hexCol = FindColumn(headers, "Hex Code");
        var orderCol = FindColumn(headers, "Display Order");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || nameCol == null)
        {
            result.Errors.Add("Colors sheet: couldn't find required columns (Color Code, Name EN).");
            return;
        }

        var existing = await _context.Colors.Where(c => c.Code != null).ToDictionaryAsync(c => c.Code!, StringComparer.OrdinalIgnoreCase);
        // Colors seeded before this importer existed have no Code — match those by Name so a
        // re-import enriches them instead of creating a parallel duplicate. Some names already
        // have multiple Code-less rows (pre-existing duplicate data); when that happens, enrich
        // whichever one is actually referenced by the most ProductColors rows (ties broken by
        // lowest Id) instead of crashing on the duplicate name or picking arbitrarily.
        var codelessColors = await _context.Colors.Where(c => c.Code == null).ToListAsync();
        var codelessIds = codelessColors.Select(c => c.Id).ToList();
        var usageCounts = await _context.ProductColors
            .Where(pc => codelessIds.Contains(pc.ColorId))
            .GroupBy(pc => pc.ColorId)
            .Select(g => new { ColorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ColorId, x => x.Count);
        var existingByName = codelessColors
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(c => usageCounts.GetValueOrDefault(c.Id)).ThenBy(c => c.Id).First(),
                StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Colors", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Colors row {rowNum}: Color Code and Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            var hex = GetString(sheet, rowNum, hexCol) ?? "#808080";
            if (!ValidateRowLengths(result.Errors, "Colors", rowNum,
                    (code, 20, "Color Code"), (name, 40, "Name EN"), (nameAr, 40, "Name AR"), (hex, 7, "Hex Code")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Colors row {rowNum}: duplicate Color Code '{code}' in this sheet.");
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            TryReadInt(sheet, rowNum, orderCol, out var displayOrder);

            if (existing.TryGetValue(code, out var color))
            {
                color.Name = name;
                color.NameAr = nameAr;
                color.HexCode = hex;
                color.DisplayOrder = displayOrder;
                color.Active = active;
                result.ColorsUpdated++;
            }
            else if (existingByName.TryGetValue(name, out color))
            {
                color.Code = code;
                color.NameAr = nameAr;
                color.HexCode = hex;
                color.DisplayOrder = displayOrder;
                color.Active = active;
                existing[code] = color;
                existingByName.Remove(name);
                result.ColorsUpdated++;
            }
            else
            {
                color = new Color
                {
                    Code = code,
                    Name = name,
                    NameAr = nameAr,
                    HexCode = hex,
                    DisplayOrder = displayOrder,
                    Active = active
                };
                _context.Colors.Add(color);
                existing[code] = color;
                result.ColorsCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportSizeGroupsAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Size Group Code");
        var nameCol = FindColumn(headers, "Group Name EN");
        var nameArCol = FindColumn(headers, "Group Name AR");
        var unitCol = FindColumn(headers, "Unit / Type");
        var notesCol = FindColumn(headers, "Notes");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || nameCol == null)
        {
            result.Errors.Add("Size Groups sheet: couldn't find required columns (Size Group Code, Group Name EN).");
            return;
        }

        var existing = await _context.SizeGroups.ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Size Groups", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Size Groups row {rowNum}: Size Group Code and Group Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            var unitType = GetString(sheet, rowNum, unitCol);
            var notes = GetString(sheet, rowNum, notesCol);
            if (!ValidateRowLengths(result.Errors, "Size Groups", rowNum,
                    (code, 30, "Size Group Code"), (name, 80, "Group Name EN"), (nameAr, 80, "Group Name AR"),
                    (unitType, 20, "Unit / Type"), (notes, 250, "Notes")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Size Groups row {rowNum}: duplicate Size Group Code '{code}' in this sheet.");
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);

            if (existing.TryGetValue(code, out var group))
            {
                group.NameEn = name;
                group.NameAr = nameAr;
                group.UnitType = unitType;
                group.Notes = notes;
                group.Active = active;
                result.SizeGroupsUpdated++;
            }
            else
            {
                group = new SizeGroup
                {
                    Code = code,
                    NameEn = name,
                    NameAr = nameAr,
                    UnitType = unitType,
                    Notes = notes,
                    Active = active
                };
                _context.SizeGroups.Add(group);
                existing[code] = group;
                result.SizeGroupsCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportSizesAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Size Code");
        var groupCol = FindColumn(headers, "Size Group Code");
        var nameCol = FindColumn(headers, "Display Name EN");
        var nameArCol = FindColumn(headers, "Display Name AR");
        var numericCol = FindColumn(headers, "Numeric Value");
        var unitCol = FindColumn(headers, "Unit");
        var sortCol = FindColumn(headers, "Sort Order");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || groupCol == null || nameCol == null)
        {
            result.Errors.Add("Sizes sheet: couldn't find required columns (Size Code, Size Group Code, Display Name EN).");
            return;
        }

        var sizeGroupsByCode = await _context.SizeGroups.ToDictionaryAsync(g => g.Code, StringComparer.OrdinalIgnoreCase);
        var existing = await _context.Sizes.Where(s => s.Code != null).ToDictionaryAsync(s => s.Code!, StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Sizes", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            var groupCode = GetString(sheet, rowNum, groupCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(groupCode))
            {
                result.Errors.Add($"Sizes row {rowNum}: Size Code, Size Group Code, and Display Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            var unit = GetString(sheet, rowNum, unitCol);
            if (!ValidateRowLengths(result.Errors, "Sizes", rowNum,
                    (code, 20, "Size Code"), (name, 20, "Display Name EN"), (nameAr, 20, "Display Name AR"), (unit, 20, "Unit")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Sizes row {rowNum}: duplicate Size Code '{code}' in this sheet.");
                continue;
            }
            if (!sizeGroupsByCode.TryGetValue(groupCode, out var sizeGroup))
            {
                var suggestion = FindClosestMatch(groupCode, sizeGroupsByCode.Keys);
                result.Errors.Add(WithSuggestion($"Sizes row {rowNum}: Size Group Code '{groupCode}' doesn't match any Size Group — import the Size Groups sheet first.", suggestion));
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            TryReadInt(sheet, rowNum, sortCol, out var sortOrder);
            decimal? numericValue = TryReadDecimal(sheet, rowNum, numericCol, out var nv) ? nv : null;

            if (existing.TryGetValue(code, out var size))
            {
                size.Label = name;
                size.LabelAr = nameAr;
                size.SizeGroupId = sizeGroup.Id;
                size.NumericValue = numericValue;
                size.Unit = unit;
                size.SortOrder = sortOrder;
                size.Active = active;
                result.SizesUpdated++;
            }
            else
            {
                size = new Size
                {
                    Code = code,
                    Label = name,
                    LabelAr = nameAr,
                    SizeGroupId = sizeGroup.Id,
                    NumericValue = numericValue,
                    Unit = unit,
                    SortOrder = sortOrder,
                    Active = active
                };
                _context.Sizes.Add(size);
                existing[code] = size;
                result.SizesCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task ImportAttributeDictionaryAsync(IXLWorksheet sheet, MasterDataImportResult result)
    {
        var headers = MapHeaders(sheet);
        var actionCol = FindColumn(headers, "Action");
        var codeCol = FindColumn(headers, "Attribute Code");
        var nameCol = FindColumn(headers, "Name EN");
        var nameArCol = FindColumn(headers, "Name AR");
        var dataTypeCol = FindColumn(headers, "Data Type");
        var filterableCol = FindColumn(headers, "Filterable");
        var activeCol = FindColumn(headers, "Active");

        if (codeCol == null || nameCol == null)
        {
            result.Errors.Add("Attribute Dictionary sheet: couldn't find required columns (Attribute Code, Name EN).");
            return;
        }

        var existing = await _context.AttributeDefinitions.ToDictionaryAsync(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var rowNum = row.RowNumber();
            var action = GetString(sheet, rowNum, actionCol);
            if (!ShouldProcessRow(action, rowNum, "Attribute Dictionary", result.Errors)) continue;

            var code = GetString(sheet, rowNum, codeCol);
            var name = GetString(sheet, rowNum, nameCol);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                result.Errors.Add($"Attribute Dictionary row {rowNum}: Attribute Code and Name EN are required.");
                continue;
            }

            var nameAr = GetString(sheet, rowNum, nameArCol);
            var dataType = GetString(sheet, rowNum, dataTypeCol);
            if (!ValidateRowLengths(result.Errors, "Attribute Dictionary", rowNum,
                    (code, 40, "Attribute Code"), (name, 80, "Name EN"), (nameAr, 80, "Name AR"), (dataType, 20, "Data Type")))
            {
                continue;
            }

            if (!seenCodes.Add(code))
            {
                result.Errors.Add($"Attribute Dictionary row {rowNum}: duplicate Attribute Code '{code}' in this sheet.");
                continue;
            }

            var active = ReadYesNo(sheet, rowNum, activeCol, true);
            var filterable = ReadYesNo(sheet, rowNum, filterableCol, false);

            if (existing.TryGetValue(code, out var attr))
            {
                attr.NameEn = name;
                attr.NameAr = nameAr;
                attr.DataType = dataType;
                attr.Filterable = filterable;
                attr.Active = active;
                result.AttributesUpdated++;
            }
            else
            {
                attr = new AttributeDefinition
                {
                    Code = code,
                    NameEn = name,
                    NameAr = nameAr,
                    DataType = dataType,
                    Filterable = filterable,
                    Active = active
                };
                _context.AttributeDefinitions.Add(attr);
                existing[code] = attr;
                result.AttributesCreated++;
            }
        }

        await _context.SaveChangesAsync();
    }
}
