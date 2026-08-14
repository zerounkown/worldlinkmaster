using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

public class ProductImportControllerTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IFormFile ToFormFile(XLWorkbook workbook)
    {
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "file", "import.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    // Regression test for the SyncStorefrontDisplayFieldsAsync cross-contamination bug: a
    // product uploaded only for a price/stock update (no rows of its own in the Media sheet)
    // must keep its existing gallery untouched, even when another product in the same file
    // does have Media rows.
    [Fact]
    public async Task Import_ProductWithNoMediaRows_KeepsExistingGalleryUntouched()
    {
        using var context = CreateContext();

        var merchant = new Merchant { UserId = "merchant-1", BusinessName = "Test Merchant", Slug = "test-merchant" };
        var category = new Category { Code = "CAT", Name = "Test Category", Slug = "test-category" };
        context.Merchants.Add(merchant);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var productA = new Product { Sku = "SKU-A", Name = "Product A", Slug = "product-a", Price = 10m, CategoryId = category.Id, MerchantId = merchant.Id, ImageUrl = "https://example.com/a-legacy.jpg" };
        var productB = new Product { Sku = "SKU-B", Name = "Product B", Slug = "product-b", Price = 20m, CategoryId = category.Id, MerchantId = merchant.Id, ImageUrl = "https://example.com/b-legacy.jpg" };
        context.Products.AddRange(productA, productB);
        await context.SaveChangesAsync();

        // Product B's pre-existing gallery — set up the way a legacy/manually-managed product
        // would look: real ProductImages rows, but no ProductMedia rows (never imported via the
        // Media sheet before). This is exactly the shape the bug wiped out.
        context.ProductImages.AddRange(
            new ProductImage { ProductId = productB.Id, ImageUrl = "https://example.com/b-legacy-1.jpg", SortOrder = 0 },
            new ProductImage { ProductId = productB.Id, ImageUrl = "https://example.com/b-legacy-2.jpg", SortOrder = 1 });
        await context.SaveChangesAsync();

        using var workbook = new XLWorkbook();
        var productsSheet = workbook.Worksheets.Add("Products");
        string[] productHeaders = { "Action", "Product Code", "Name EN", "Main Category Code", "Default Price Excl. VAT" };
        for (var i = 0; i < productHeaders.Length; i++) productsSheet.Cell(1, i + 1).Value = productHeaders[i];
        // Row 2: SKU-A, price update, will also get a Media row below.
        productsSheet.Cell(2, 1).Value = "UPDATE";
        productsSheet.Cell(2, 2).Value = "SKU-A";
        productsSheet.Cell(2, 3).Value = "Product A";
        productsSheet.Cell(2, 4).Value = "CAT";
        productsSheet.Cell(2, 5).Value = 11m;
        // Row 3: SKU-B, price update ONLY — no Media row for this product anywhere in the file.
        productsSheet.Cell(3, 1).Value = "UPDATE";
        productsSheet.Cell(3, 2).Value = "SKU-B";
        productsSheet.Cell(3, 3).Value = "Product B";
        productsSheet.Cell(3, 4).Value = "CAT";
        productsSheet.Cell(3, 5).Value = 21m;

        var mediaSheet = workbook.Worksheets.Add("Media");
        string[] mediaHeaders = { "Action", "Product Code", "Media Scope", "Media Type", "Media URL", "Display Order", "Active" };
        for (var i = 0; i < mediaHeaders.Length; i++) mediaSheet.Cell(1, i + 1).Value = mediaHeaders[i];
        mediaSheet.Cell(2, 1).Value = "UPDATE";
        mediaSheet.Cell(2, 2).Value = "SKU-A";
        mediaSheet.Cell(2, 3).Value = "Shared";
        mediaSheet.Cell(2, 4).Value = "Image";
        mediaSheet.Cell(2, 5).Value = "https://example.com/a-new-1.jpg";
        mediaSheet.Cell(2, 6).Value = 1;
        mediaSheet.Cell(2, 7).Value = "Yes";

        var file = ToFormFile(workbook);
        var controller = new ProductImportController(context);

        var viewResult = await controller.Import(file);
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(viewResult).Model as WorldLinkMaster.Web.Models.ViewModels.ProductImportResult;

        Assert.NotNull(result);
        Assert.Empty(result!.Errors);

        // SKU-A: had a Media row in this upload — its gallery should be rebuilt from it.
        var refreshedA = await context.Products.Include(p => p.Images).FirstAsync(p => p.Sku == "SKU-A");
        Assert.Equal("https://example.com/a-new-1.jpg", refreshedA.ImageUrl);
        Assert.Single(refreshedA.Images);
        Assert.Equal("https://example.com/a-new-1.jpg", refreshedA.Images.First().ImageUrl);

        // SKU-B: no Media row in this upload — its pre-existing gallery must be untouched,
        // even though SKU-A (same file) had media, and SKU-B was itself present in the
        // Products sheet for a price update.
        var refreshedB = await context.Products.Include(p => p.Images).FirstAsync(p => p.Sku == "SKU-B");
        Assert.Equal(21m, refreshedB.Price); // the price update itself did apply
        Assert.Equal("https://example.com/b-legacy.jpg", refreshedB.ImageUrl); // cover image untouched
        Assert.Equal(2, refreshedB.Images.Count); // gallery rows untouched
        Assert.Contains(refreshedB.Images, i => i.ImageUrl == "https://example.com/b-legacy-1.jpg");
        Assert.Contains(refreshedB.Images, i => i.ImageUrl == "https://example.com/b-legacy-2.jpg");
    }
}
