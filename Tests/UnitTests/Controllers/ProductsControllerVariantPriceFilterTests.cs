using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Tests.UnitTests.Controllers;

/// <summary>
/// ProductsController.ApplyVariantPriceFilter — the listing page's variant-aware price filter,
/// extracted out of Index() specifically so it's testable on SQLite at all (Index() itself
/// unconditionally computes price-range slider bounds via MinAsync()/MaxAsync() over a decimal
/// column, which SQLite's EF Core provider can't translate — see
/// ProductsControllerTests.IndexSkipReason in Tests/IntegrationTests). This filter is plain
/// WHERE-clause decimal comparisons, no aggregates, so it runs fine here.
///
/// Mirrors the real production scenario reported for the listing page's color/price filters:
/// "24-7 Agility Pant" priced 530.25 for most colors, but 472.50 for Ranger Green (see
/// Data/SeedData.cs, where the same product is now seeded for the Playwright E2E suite).
/// </summary>
public class ProductsControllerVariantPriceFilterTests
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

    private static int SeedAgilityPant(ApplicationDbContext context)
    {
        var category = new Category { Code = "APP", Name = "Tactical Apparel", Slug = "tactical-apparel" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        context.Categories.Add(category);
        context.Users.Add(user);
        context.Merchants.Add(merchant);

        var greenOlive = new ColorFamily { Code = "green-olive", Name = "Green/Olive", HexCode = "#4b5320", DisplayOrder = 1 };
        var black = new ColorFamily { Code = "black", Name = "Black", HexCode = "#1c1c1c", DisplayOrder = 2 };
        var tanCoyote = new ColorFamily { Code = "tan-coyote", Name = "Tan / Coyote", HexCode = "#b08d57", DisplayOrder = 3 };
        context.ColorFamilies.AddRange(greenOlive, black, tanCoyote);
        context.SaveChanges();

        var rangerGreen = new Color { Name = "Ranger Green", HexCode = "#4b5320", FamilyId = greenOlive.Id };
        var blackColor = new Color { Name = "Black", HexCode = "#1c1c1c", FamilyId = black.Id };
        var coyoteTan = new Color { Name = "Coyote Tan", HexCode = "#b08d57", FamilyId = tanCoyote.Id };
        context.Colors.AddRange(rangerGreen, blackColor, coyoteTan);
        context.SaveChanges();

        var product = new Product
        {
            Name = "24-7 Agility Pant",
            Slug = "24-7-agility-pant",
            Sku = "WLM-APP-006",
            Price = 530.25m,
            StockQuantity = 30,
            CategoryId = category.Id,
            MerchantId = merchant.Id,
            IsPublished = true
        };
        context.Products.Add(product);
        context.SaveChanges();

        context.ProductVariants.AddRange(
            new ProductVariant { ProductId = product.Id, ColorId = blackColor.Id, Sku = "WLM-APP-006-BLK", StockQuantity = 10, Active = true },
            new ProductVariant { ProductId = product.Id, ColorId = coyoteTan.Id, Sku = "WLM-APP-006-CYT", StockQuantity = 10, Active = true },
            // Ranger Green undercuts every other color — the real reported scenario.
            new ProductVariant { ProductId = product.Id, ColorId = rangerGreen.Id, Sku = "WLM-APP-006-RGR", StockQuantity = 10, Active = true, Price = 472.50m });
        context.SaveChanges();

        return greenOlive.Id;
    }

    private static IQueryable<Product> BaseQuery(ApplicationDbContext context) =>
        context.Products.AsNoTracking().Where(p => p.IsPublished).Include(p => p.Variants).ThenInclude(v => v.Color);

    [Fact]
    public async Task GreenOliveFilter_RangeAroundBasePrice_ExcludesProduct()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            var greenOliveFamilyId = SeedAgilityPant(context);

            // Ranger Green is 472.50 — outside 530..540 — and it's the only color in scope once
            // Green/Olive is the selected family, so the product must not match at all.
            var result = ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 530m, maxPriceAed: 540m,
                selectedFamilyIds: new List<int> { greenOliveFamilyId }, otherFamilyId: -1);

            Assert.Empty(await result.ToListAsync());
        }
    }

    [Fact]
    public async Task GreenOliveFilter_RangeAroundRangerGreenPrice_IncludesProduct()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            var greenOliveFamilyId = SeedAgilityPant(context);

            var result = ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 470m, maxPriceAed: 480m,
                selectedFamilyIds: new List<int> { greenOliveFamilyId }, otherFamilyId: -1);

            Assert.Single(await result.ToListAsync());
        }
    }

    [Fact]
    public async Task NoColorFilter_RangeAroundBasePrice_IncludesProduct_ViaOtherColors()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            SeedAgilityPant(context);

            // No color filter: Black/Coyote Tan are still 530.25, so 530..540 matches via them
            // even though Ranger Green wouldn't.
            var result = ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 530m, maxPriceAed: 540m,
                selectedFamilyIds: new List<int>(), otherFamilyId: -1);

            Assert.Single(await result.ToListAsync());
        }
    }

    [Fact]
    public async Task NoColorFilter_RangeAroundRangerGreenPrice_IncludesProduct_ViaThatOneVariant()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            SeedAgilityPant(context);

            // No color filter: the product still matches 470..480 through its Ranger Green
            // variant alone, even though every other color is priced well outside that range —
            // "at least one variant in range", not "every variant" or "the base price".
            var result = ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 470m, maxPriceAed: 480m,
                selectedFamilyIds: new List<int>(), otherFamilyId: -1);

            Assert.Single(await result.ToListAsync());
        }
    }

    [Fact]
    public async Task BlackFilter_RangeAroundRangerGreenPrice_ExcludesProduct()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            SeedAgilityPant(context);
            var blackFamilyId = context.ColorFamilies.Single(f => f.Code == "black").Id;

            // Filtering Black specifically: Black is 530.25, so a 470..480 range must exclude the
            // product even though a DIFFERENT color (Ranger Green) would have matched.
            var result = ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 470m, maxPriceAed: 480m,
                selectedFamilyIds: new List<int> { blackFamilyId }, otherFamilyId: -1);

            Assert.Empty(await result.ToListAsync());
        }
    }

    [Fact]
    public async Task MinOnly_StillAppliesVariantAwareLogic()
    {
        var (context, connection) = CreateContext();
        using (connection)
        {
            var greenOliveFamilyId = SeedAgilityPant(context);

            // Only a floor set, no ceiling — Green/Olive's 472.50 clears a 500 floor? No, so it
            // should be excluded; clears a 400 floor, so it should be included.
            var excluded = await ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 500m, maxPriceAed: null,
                selectedFamilyIds: new List<int> { greenOliveFamilyId }, otherFamilyId: -1).ToListAsync();
            Assert.Empty(excluded);

            var included = await ProductsController.ApplyVariantPriceFilter(
                BaseQuery(context), minPriceAed: 400m, maxPriceAed: null,
                selectedFamilyIds: new List<int> { greenOliveFamilyId }, otherFamilyId: -1).ToListAsync();
            Assert.Single(included);
        }
    }
}
