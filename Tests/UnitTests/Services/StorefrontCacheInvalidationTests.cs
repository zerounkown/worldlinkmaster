using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorldLinkMaster.Web.Areas.Admin.Controllers;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Services;

/// <summary>
/// End-to-end check that the admin actions listed in IStorefrontCacheService's doc comment
/// (the ones that write data the mega-menu/listing cache reads) actually call Invalidate() —
/// not just that Invalidate() itself works (see StorefrontCacheServiceTests for that). Each test
/// here warms the real cache, performs the admin write through the real controller action, and
/// asserts a subsequent read reflects the change instead of the stale cached snapshot.
/// </summary>
public class StorefrontCacheInvalidationTests
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

    private static Mock<IStringLocalizer<SharedResource>> CreateLocalizer()
    {
        var localizer = new Mock<IStringLocalizer<SharedResource>>();
        localizer.Setup(l => l[It.IsAny<string>()])
            .Returns((string name) => new LocalizedString(name, name));
        localizer.Setup(l => l[It.IsAny<string>(), It.IsAny<object[]>()])
            .Returns((string name, object[] args) => new LocalizedString(name, string.Format(name, args)));
        return localizer;
    }

    [Fact]
    public async Task CategoriesController_Create_InvalidatesTheNavMenuCache()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        context.Categories.Add(new Category { Name = "Footwear", Slug = "footwear" });
        context.SaveChanges();

        var cache = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var warmed = await cache.GetNavMenuDataAsync();
        Assert.Single(warmed.NavCategories);

        var httpContext = new DefaultHttpContext();
        var controller = new CategoriesController(context, CreateLocalizer().Object, cache)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        await controller.Create(new Category { Name = "Bags", Slug = "bags" });

        var afterCreate = await cache.GetNavMenuDataAsync();
        Assert.Equal(2, afterCreate.NavCategories.Count);
    }

    [Fact]
    public async Task CategoriesController_DeleteConfirmed_InvalidatesTheNavMenuCache()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        var category = new Category { Name = "Footwear", Slug = "footwear" };
        context.Categories.Add(category);
        context.SaveChanges();

        var cache = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var warmed = await cache.GetNavMenuDataAsync();
        Assert.Single(warmed.NavCategories);

        var httpContext = new DefaultHttpContext();
        var controller = new CategoriesController(context, CreateLocalizer().Object, cache)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        await controller.DeleteConfirmed(category.Id);

        var afterDelete = await cache.GetNavMenuDataAsync();
        Assert.Empty(afterDelete.NavCategories);
    }

    [Fact]
    public async Task AdminProductsController_Edit_InvalidatesTheNavMenuCache()
    {
        var (context, connection) = CreateContext();
        using var _ = connection;
        var category = new Category { Name = "Footwear", Slug = "footwear" };
        var user = new ApplicationUser { Id = "merchant-1", UserName = "merchant1@example.com", Email = "merchant1@example.com" };
        var merchant = new Merchant { UserId = user.Id, BusinessName = "Test Merchant", Slug = "test-merchant" };
        context.Categories.Add(category);
        context.Users.Add(user);
        context.Merchants.Add(merchant);
        context.SaveChanges();

        var product = new Product
        {
            Name = "Combat Boot", Slug = "combat-boot", Sku = "BOOT-1", Price = 100m, StockQuantity = 5,
            IsPublished = true, IsFeatured = false, CategoryId = category.Id, MerchantId = merchant.Id
        };
        context.Products.Add(product);
        context.SaveChanges();

        var cache = new StorefrontCacheService(context, new MemoryCache(new MemoryCacheOptions()));
        var warmed = await cache.GetNavMenuDataAsync();
        Assert.True(warmed.CategoryPreviews.TryGetValue(category.Id, out var before));
        Assert.Single(before!); // published + in stock, so it's in the preview before the edit

        var outputCache = new Mock<IOutputCacheStore>();
        outputCache.Setup(o => o.EvictByTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        var httpContext = new DefaultHttpContext();
        var controller = new WorldLinkMaster.Web.Areas.Admin.Controllers.ProductsController(
            context, CreateLocalizer().Object, outputCache.Object, NullLogger<WorldLinkMaster.Web.Areas.Admin.Controllers.ProductsController>.Instance, cache)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };

        // Edit() copies StockQuantity onto the tracked entity — going out of stock must drop it
        // from the cached preview once the cache is invalidated, not keep showing it stale.
        product.StockQuantity = 0;
        await controller.Edit(product.Id, product);

        var afterEdit = await cache.GetNavMenuDataAsync();
        // No candidates left for this category at all now, so (same as the view's own
        // TryGetValue(...) ?? empty-list fallback) there may be no dictionary entry rather than
        // an empty one — either is a correct "nothing to preview" result.
        var after = afterEdit.CategoryPreviews.TryGetValue(category.Id, out var preview) ? preview : new List<Product>();
        Assert.Empty(after);
    }
}
