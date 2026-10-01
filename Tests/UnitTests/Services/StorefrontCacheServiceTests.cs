using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Services;

/// <summary>
/// StorefrontCacheService: caches the data Views/Shared/_Layout.cshtml (every storefront page)
/// and ProductsController.Index (the color filter) used to re-query from Postgres on every
/// single request — see the perf/reduce-db-egress PR description for the egress investigation
/// this came out of. Covers: results are actually cached (a DB change between two calls isn't
/// visible until Invalidate()), Invalidate() clears it, and the categoryPreviews N+1 loop this
/// replaced (one query per category) now produces the same per-category "top 4, featured first"
/// shape from a single collapsed query.
/// </summary>
public class StorefrontCacheServiceTests
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

    private static Merchant SeedMerchant(ApplicationDbContext context)
    {
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        context.Users.Add(user);
        context.Merchants.Add(merchant);
        context.SaveChanges();
        return merchant;
    }

    [Fact]
    public async Task GetNavMenuDataAsync_SecondCall_ReturnsCachedResult_DoesNotSeeADatabaseChangeMadeBetweenCalls()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        var merchant = SeedMerchant(context);
        var category = new Category { Name = "Footwear", Slug = "footwear" };
        context.Categories.Add(category);
        context.SaveChanges();
        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));

        var first = await service.GetNavMenuDataAsync();
        Assert.Single(first.NavCategories);

        // A category added after the first call, with no Invalidate() in between — a real
        // request hitting this same cached instance should still see the OLD snapshot.
        context.Categories.Add(new Category { Name = "Bags", Slug = "bags" });
        context.SaveChanges();

        var second = await service.GetNavMenuDataAsync();

        Assert.Single(second.NavCategories);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task Invalidate_ClearsCache_NextCallSeesTheDatabaseChange()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        SeedMerchant(context);
        context.Categories.Add(new Category { Name = "Footwear", Slug = "footwear" });
        context.SaveChanges();
        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));

        await service.GetNavMenuDataAsync();
        context.Categories.Add(new Category { Name = "Bags", Slug = "bags" });
        context.SaveChanges();
        service.Invalidate();

        var afterInvalidate = await service.GetNavMenuDataAsync();

        Assert.Equal(2, afterInvalidate.NavCategories.Count);
    }

    [Fact]
    public async Task GetColorFamiliesAsync_IsCachedSeparatelyFromNavMenuData_AndInvalidateClearsBoth()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        context.ColorFamilies.Add(new ColorFamily { Code = "black", Name = "Black", HexCode = "#000000", DisplayOrder = 1 });
        context.SaveChanges();
        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));

        var first = await service.GetColorFamiliesAsync();
        Assert.Single(first);

        context.ColorFamilies.Add(new ColorFamily { Code = "tan", Name = "Tan", HexCode = "#b08d57", DisplayOrder = 2 });
        context.SaveChanges();
        var stillCached = await service.GetColorFamiliesAsync();
        Assert.Single(stillCached);

        service.Invalidate();
        var afterInvalidate = await service.GetColorFamiliesAsync();
        Assert.Equal(2, afterInvalidate.Count);
    }

    [Fact]
    public async Task GetNavMenuDataAsync_CategoryPreviews_CapsAtFourPerCategory_FeaturedFirst_ScopedToThatCategoryOnly()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        var merchant = SeedMerchant(context);
        var footwear = new Category { Name = "Footwear", Slug = "footwear" };
        var bags = new Category { Name = "Bags", Slug = "bags" };
        context.Categories.AddRange(footwear, bags);
        context.SaveChanges();

        // 5 published, in-stock footwear products — only 4 should come back, featured ones first
        // (then newest Id), exactly like the old per-category Take(4) loop.
        for (var i = 1; i <= 5; i++)
        {
            context.Products.Add(new Product
            {
                Name = $"Boot {i}",
                Slug = $"boot-{i}",
                Sku = $"BOOT-{i}",
                Price = 100m,
                StockQuantity = 5,
                IsPublished = true,
                IsFeatured = i == 5, // the last-created one is the only featured product
                CategoryId = footwear.Id,
                MerchantId = merchant.Id
            });
        }
        // Out of stock — must never appear in previews.
        context.Products.Add(new Product
        {
            Name = "Out of Stock Boot", Slug = "oos-boot", Sku = "BOOT-OOS", Price = 100m, StockQuantity = 0,
            IsPublished = true, CategoryId = footwear.Id, MerchantId = merchant.Id
        });
        // A single Bags product — must not leak into Footwear's preview list.
        context.Products.Add(new Product
        {
            Name = "Duffel Bag", Slug = "duffel-bag", Sku = "BAG-1", Price = 80m, StockQuantity = 3,
            IsPublished = true, CategoryId = bags.Id, MerchantId = merchant.Id
        });
        context.SaveChanges();

        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var data = await service.GetNavMenuDataAsync();

        Assert.True(data.CategoryPreviews.TryGetValue(footwear.Id, out var footwearPreview));
        Assert.Equal(4, footwearPreview!.Count);
        Assert.Equal("Boot 5", footwearPreview[0].Name); // the featured one sorts first
        Assert.DoesNotContain(footwearPreview, p => p.Name == "Out of Stock Boot");

        Assert.True(data.CategoryPreviews.TryGetValue(bags.Id, out var bagsPreview));
        Assert.Single(bagsPreview!);
        Assert.Equal("Duffel Bag", bagsPreview![0].Name);
    }

    [Fact]
    public async Task GetNavMenuDataAsync_NavCategories_PreservesDescriptionAndDescriptionAr()
    {
        // Regression guard: an earlier version of LoadNavMenuDataAsync's .Select() projection
        // omitted Category/Subcategory.Description(Ar) entirely, which silently broke the
        // generic per-category mega-menu's promo caption (_Layout.cshtml falls back to showing
        // the subcategory's name instead when LocalizedDescription() comes back empty) for every
        // category except Apparel's own hand-curated menu.
        var (context, connection) = CreateContext();
        using var _ = connection;
        var category = new Category { Name = "Footwear", Slug = "footwear", Description = "Boots built for the field.", DescriptionAr = "أحذية مصممة للميدان." };
        context.Categories.Add(category);
        context.SaveChanges();
        var subcategory = new Subcategory { Name = "Boots", Slug = "boots", CategoryId = category.Id, Description = "Rugged all-terrain boots.", DescriptionAr = "أحذية وعرة لجميع التضاريس." };
        context.Subcategories.Add(subcategory);
        context.SaveChanges();

        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var data = await service.GetNavMenuDataAsync();

        var cachedCategory = Assert.Single(data.NavCategories);
        Assert.Equal("Boots built for the field.", cachedCategory.Description);
        Assert.Equal("أحذية مصممة للميدان.", cachedCategory.DescriptionAr);
        var cachedSubcategory = Assert.Single(cachedCategory.Subcategories);
        Assert.Equal("Rugged all-terrain boots.", cachedSubcategory.Description);
        Assert.Equal("أحذية وعرة لجميع التضاريس.", cachedSubcategory.DescriptionAr);
    }

    [Fact]
    public async Task GetNavMenuDataAsync_SubcategoryNotInAnyHardcodedFamily_StillHasPublishedProductsFlaggedCorrectly()
    {
        // Mirrors the real Headwear bug: a subcategory under Apparel that the hand-curated
        // Apparel mega-menu (BuildApparelMenuAsync in _Layout.cshtml) never references by slug —
        // the data this service provides must still correctly flag it as "has published
        // products" so the view's own leftover-column logic can pick it up. This test covers the
        // cached DATA only; Tests.E2E/Journeys/ApparelMegaMenuHeadwearTests.cs covers the actual
        // rendered menu column end to end.
        var (context, connection) = CreateContext();
        using var _ = connection;
        var merchant = SeedMerchant(context);
        var apparel = new Category { Name = "Tactical Apparel", Slug = "tactical-apparel" };
        context.Categories.Add(apparel);
        context.SaveChanges();
        var headwear = new Subcategory { Name = "Headwear", Slug = "headwear", CategoryId = apparel.Id };
        context.Subcategories.Add(headwear);
        context.SaveChanges();
        context.Products.Add(new Product
        {
            Name = "Tactical Boonie", Slug = "tactical-boonie", Sku = "F5519", Price = 59.99m, StockQuantity = 10,
            IsPublished = true, CategoryId = apparel.Id, SubcategoryId = headwear.Id, MerchantId = merchant.Id
        });
        context.SaveChanges();

        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var data = await service.GetNavMenuDataAsync();

        Assert.Contains(headwear.Id, data.SubcategoriesWithPublishedProducts);
        var cachedApparel = Assert.Single(data.NavCategories);
        Assert.Contains(cachedApparel.Subcategories, s => s.Slug == "headwear");
    }

    [Fact]
    public async Task GetNavMenuDataAsync_QuickFilterProducts_OnlyIncludesTShirtAndPantsFamilySubcategories()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        var merchant = SeedMerchant(context);
        var apparel = new Category { Name = "Apparel", Slug = "tactical-apparel" };
        context.Categories.Add(apparel);
        context.SaveChanges();

        var tShirts = new Subcategory { Name = "T-Shirts", Slug = "t-shirts", CategoryId = apparel.Id };
        var jackets = new Subcategory { Name = "Jackets", Slug = "tactical-jackets", CategoryId = apparel.Id };
        context.Subcategories.AddRange(tShirts, jackets);
        context.SaveChanges();

        context.Products.Add(new Product
        {
            Name = "Combat T-Shirt", Slug = "combat-tshirt", Sku = "TS-1", Price = 50m, StockQuantity = 10,
            IsPublished = true, CategoryId = apparel.Id, SubcategoryId = tShirts.Id, MerchantId = merchant.Id
        });
        context.Products.Add(new Product
        {
            Name = "Field Jacket", Slug = "field-jacket", Sku = "JK-1", Price = 150m, StockQuantity = 10,
            IsPublished = true, CategoryId = apparel.Id, SubcategoryId = jackets.Id, MerchantId = merchant.Id
        });
        context.SaveChanges();

        var service = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var data = await service.GetNavMenuDataAsync();

        Assert.Single(data.QuickFilterProducts);
        Assert.Equal("t-shirts", data.QuickFilterProducts[0].Subcategory?.Slug);
    }
}
