using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// Production incident: colors like "Black" (x5), "Charcoal" (x6), "Coyote Tan" (x5) ended up
/// with multiple duplicate rows, created in batches with consecutive Ids across SEPARATE Master
/// Data import runs. Root cause: ImportColorsAsync/ImportBrandsAsync's by-name fallback match
/// only ever checked colors/brands that had NO Code yet ("Colors seeded before this importer
/// existed have no Code") — once ANY import run gave a color/brand a Code, a LATER submission
/// using a different code for the same name would miss both the Code-based match (different
/// code) and the old Name-based fallback (no longer code-less), and mint a fresh duplicate.
/// Fixed by matching by name (ignoring case and extra whitespace) against ALL existing
/// colors/brands/sizes, not just code-less ones, preferring an Active one then the lowest Id.
/// </summary>
public class MasterDataControllerDedupeTests
{
    private static (ApplicationDbContext Context, SqliteConnection Connection) CreateContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        var context = new ApplicationDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    private static MasterDataController CreateController(ApplicationDbContext context) =>
        new(context, NullLogger<MasterDataController>.Instance, new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions())));

    private static IFormFile ToFormFile(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "wlm-test.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    private static XLWorkbook NewColorsWorkbook(params (string Code, string Name, string? NameAr, string? Hex, bool Active)[] rows)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Colors");
        string[] headers = { "Action", "Color Code", "Name EN", "Name AR", "Hex Code", "Display Order", "Active" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = "ADD";
            sheet.Cell(row, 2).Value = r.Code;
            sheet.Cell(row, 3).Value = r.Name;
            if (r.NameAr != null) sheet.Cell(row, 4).Value = r.NameAr;
            sheet.Cell(row, 5).Value = r.Hex ?? "#808080";
            sheet.Cell(row, 6).Value = 0;
            sheet.Cell(row, 7).Value = r.Active ? "Yes" : "No";
            row++;
        }
        return workbook;
    }

    private static XLWorkbook NewBrandsWorkbook(params (string Code, string Name, string? NameAr, bool Active)[] rows)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Brands");
        string[] headers = { "Action", "Brand Code", "Brand Name EN", "Brand Name AR", "Website", "Active" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = "ADD";
            sheet.Cell(row, 2).Value = r.Code;
            sheet.Cell(row, 3).Value = r.Name;
            if (r.NameAr != null) sheet.Cell(row, 4).Value = r.NameAr;
            sheet.Cell(row, 6).Value = r.Active ? "Yes" : "No";
            row++;
        }
        return workbook;
    }

    [Fact]
    public async Task ImportColors_SameNameDifferentCodeAcrossSeparateRuns_DifferentCaseAndSpacing_ReusesOneColor()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var controller = CreateController(context);

        using (var workbook1 = NewColorsWorkbook(("BLK-A", "Black", "أسود", "#1c1c1c", true)))
        {
            var result1 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook1))).Model);
            Assert.Empty(result1.Errors);
            Assert.Equal(1, result1.ColorsCreated);
        }

        // A later batch gives the SAME color a DIFFERENT code, with different case and a doubled
        // internal space — exactly the shape that let the old code-less-only fallback miss it.
        using (var workbook2 = NewColorsWorkbook(("BLK-B", "black", null, "#1c1c1c", true)))
        {
            var result2 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook2))).Model);
            Assert.Empty(result2.Errors);
            Assert.Equal(0, result2.ColorsCreated);
            Assert.Equal(1, result2.ColorsUpdated);
        }

        var color = await context.Colors.SingleAsync();
        Assert.Equal("BLK-B", color.Code); // enriched with the later run's code
        Assert.Equal("Black", color.Name); // original spelling preserved — the name-based match only enriches Code/NameAr/HexCode/DisplayOrder/Active, same as the pre-existing code-less fallback already did
    }

    [Fact]
    public async Task ImportColors_NamesCollideOnlyByInternalSpacing_StillMatchAsOneColor()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        context.Colors.Add(new Color { Code = "CT-1", Name = "Coyote Tan", HexCode = "#b08d57", Active = true });
        context.SaveChanges();

        using var workbook = NewColorsWorkbook(("CT-2", "Coyote  Tan", "بني كويوت", "#b08d57", true)); // double space
        var controller = CreateController(context);
        var result = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook))).Model);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.ColorsCreated);
        Assert.Equal(1, result.ColorsUpdated);
        Assert.Equal(1, await context.Colors.CountAsync());
    }

    [Fact]
    public async Task ImportColors_NameMatchesMultipleExistingColors_PrefersActiveThenLowestId()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var inactiveOlder = new Color { Code = "RG-OLD", Name = "Ranger Green", HexCode = "#4b5320", Active = false };
        context.Colors.Add(inactiveOlder);
        context.SaveChanges();
        var activeNewer = new Color { Code = "RG-NEW", Name = "ranger green", HexCode = "#4b5320", Active = true };
        context.Colors.Add(activeNewer);
        context.SaveChanges();
        Assert.True(inactiveOlder.Id < activeNewer.Id);

        // Yet another code for the same (normalized) name — must enrich the ACTIVE existing
        // color, not the older/lower-Id inactive one.
        using var workbook = NewColorsWorkbook(("RG-NEWEST", "Ranger Green", null, "#4b5320", true));
        var controller = CreateController(context);
        var result = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook))).Model);

        Assert.Empty(result.Errors);
        Assert.Equal(0, result.ColorsCreated);
        Assert.Equal(2, await context.Colors.CountAsync()); // no new row, and the inactive duplicate is untouched
        var reloadedActive = await context.Colors.AsNoTracking().FirstAsync(c => c.Id == activeNewer.Id);
        Assert.Equal("RG-NEWEST", reloadedActive.Code);
        var reloadedInactive = await context.Colors.AsNoTracking().FirstAsync(c => c.Id == inactiveOlder.Id);
        Assert.Equal("RG-OLD", reloadedInactive.Code); // left alone
    }

    private static XLWorkbook NewSizesWorkbook(string groupCode, params (string Code, string Name, int SortOrder, bool Active)[] rows)
    {
        var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Sizes");
        string[] headers = { "Action", "Size Code", "Size Group Code", "Display Name EN", "Display Name AR", "Numeric Value", "Unit", "Sort Order", "Active" };
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];

        var row = 2;
        foreach (var r in rows)
        {
            sheet.Cell(row, 1).Value = "ADD";
            sheet.Cell(row, 2).Value = r.Code;
            sheet.Cell(row, 3).Value = groupCode;
            sheet.Cell(row, 4).Value = r.Name;
            sheet.Cell(row, 8).Value = r.SortOrder;
            sheet.Cell(row, 9).Value = r.Active ? "Yes" : "No";
            row++;
        }
        return workbook;
    }

    [Fact]
    public async Task ImportSizes_SameLabelDifferentCodeInSameGroupAcrossSeparateRuns_ReusesOneSize()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var group = new SizeGroup { Code = "TR-WL", NameEn = "Trouser Waist x Length" };
        context.SizeGroups.Add(group);
        context.SaveChanges();

        var controller = CreateController(context);

        using (var workbook1 = NewSizesWorkbook("TR-WL", ("TRWL-A", "32x30", 0, true)))
        {
            var result1 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook1))).Model);
            Assert.Empty(result1.Errors);
            Assert.Equal(1, result1.SizesCreated);
        }

        // A later batch gives the SAME size a DIFFERENT code — must reuse it, not duplicate it.
        using (var workbook2 = NewSizesWorkbook("TR-WL", ("TRWL-B", "32X30", 1, true)))
        {
            var result2 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook2))).Model);
            Assert.Empty(result2.Errors);
            Assert.Equal(0, result2.SizesCreated);
            Assert.Equal(1, result2.SizesUpdated);
        }

        var size = await context.Sizes.SingleAsync();
        Assert.Equal("TRWL-B", size.Code);
    }

    [Fact]
    public async Task ImportSizes_SameLabel_DifferentSizeGroups_AreNotMergedTogether()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var hatGroup = new SizeGroup { Code = "HAT", NameEn = "Hat Size" };
        var shoeGroup = new SizeGroup { Code = "SHOE", NameEn = "Footwear" };
        context.SizeGroups.AddRange(hatGroup, shoeGroup);
        context.SaveChanges();
        context.Sizes.Add(new Size { Code = "HAT-7", Label = "7", SizeGroupId = hatGroup.Id, Active = true });
        context.SaveChanges();

        // Same label "7", but a DIFFERENT size group — must create a new, separate Size, not
        // reuse the Hat group's "7".
        using var workbook = NewSizesWorkbook("SHOE", ("SHOE-7", "7", 0, true));
        var controller = CreateController(context);
        var result = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook))).Model);

        Assert.Empty(result.Errors);
        Assert.Equal(1, result.SizesCreated);
        Assert.Equal(2, await context.Sizes.CountAsync());
    }

    [Fact]
    public async Task ImportBrands_SameNameDifferentCodeAcrossSeparateRuns_ReusesOneBrand()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        using var __ = context;
        var controller = CreateController(context);

        using (var workbook1 = NewBrandsWorkbook(("CON-A", "Condor", "كوندور", true)))
        {
            var result1 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook1))).Model);
            Assert.Empty(result1.Errors);
            Assert.Equal(1, result1.BrandsCreated);
        }

        using (var workbook2 = NewBrandsWorkbook(("CON-B", "condor", null, true)))
        {
            var result2 = Assert.IsType<MasterDataImportResult>(Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await controller.Import(ToFormFile(workbook2))).Model);
            Assert.Empty(result2.Errors);
            Assert.Equal(0, result2.BrandsCreated);
            Assert.Equal(1, result2.BrandsUpdated);
        }

        var brand = await context.Brands.SingleAsync();
        Assert.Equal("CON-B", brand.Code);
    }
}
