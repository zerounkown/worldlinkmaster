using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public class StorefrontCacheService : IStorefrontCacheService
{
    private const string NavMenuDataCacheKey = "StorefrontCache:NavMenuData";
    private const string ColorFamiliesCacheKey = "StorefrontCache:ColorFamilies";

    // Near-static reference data — categories/subcategories/brands change only through
    // deliberate admin actions (all of which call Invalidate()), so this is purely a bound on
    // how stale a SECOND App Service instance's own copy can get if it never receives the
    // in-process Invalidate() call a write on the OTHER instance made (see the interface doc).
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    // Single source of truth for which subcategory slugs/attribute codes feed the Apparel
    // mega-menu's quick-filter chips — shared with Views/Shared/_Layout.cshtml, which groups
    // QuickFilterProducts/RealApparelAttrPairs by these same values. Keeping them here (rather
    // than duplicated in the view) means the query and the view's grouping can't drift apart.
    public static readonly string[] TShirtFamilySlugs =
    {
        "t-shirts", "combat-shirts", "combat-shirt", "short-sleeve-t-shirt", "long-sleeve-t-shirt",
        "short-sleeve-crew-neck-t-shirt", "long-sleeve-crew-neck-t-shirt"
    };

    public static readonly string[] PantsFamilySlugs = { "tactical-pants", "tactical-shorts", "tactical-trousers" };

    public static readonly string[] ApparelAttrCodes =
    {
        "USE-CASE", "NECK-TYPE", "SLEEVE-LENGTH", "MATERIAL", "PANTS-TYPE", "JACKET-TYPE", "WATER_RES", "UNIFORM-TYPE"
    };

    public static readonly string[] ApparelMenuBrandCodes = { "WLM", "CON", "PRO", "TRS", "ROT" };

    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    public StorefrontCacheService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<NavMenuData> GetNavMenuDataAsync()
    {
        if (_cache.TryGetValue(NavMenuDataCacheKey, out NavMenuData? cached) && cached != null)
        {
            return cached;
        }

        var data = await LoadNavMenuDataAsync();
        _cache.Set(NavMenuDataCacheKey, data, CacheTtl);
        return data;
    }

    public async Task<List<ColorFamily>> GetColorFamiliesAsync()
    {
        if (_cache.TryGetValue(ColorFamiliesCacheKey, out List<ColorFamily>? cached) && cached != null)
        {
            return cached;
        }

        var families = await _context.ColorFamilies.AsNoTracking().OrderBy(f => f.DisplayOrder).ToListAsync();
        _cache.Set(ColorFamiliesCacheKey, families, CacheTtl);
        return families;
    }

    public void Invalidate()
    {
        _cache.Remove(NavMenuDataCacheKey);
        _cache.Remove(ColorFamiliesCacheKey);
    }

    private async Task<NavMenuData> LoadNavMenuDataAsync()
    {
        // Only the columns Views/Shared/_Layout.cshtml's mega-menu/side-nav actually render —
        // notably no Description/DescriptionAr, which is what the old Include-based version of
        // this query used to pull for nothing (Category.Description is small/bounded, but this
        // keeps the query and the Product-shaped ones below built the same deliberate way).
        var navCategories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new Category
            {
                Id = c.Id,
                Name = c.Name,
                NameAr = c.NameAr,
                Slug = c.Slug,
                ImageUrl = c.ImageUrl,
                DisplayOrder = c.DisplayOrder,
                Subcategories = c.Subcategories
                    .OrderBy(s => s.Name)
                    .Select(s => new Subcategory
                    {
                        Id = s.Id,
                        Name = s.Name,
                        NameAr = s.NameAr,
                        Slug = s.Slug,
                        ImageUrl = s.ImageUrl,
                        CategoryId = s.CategoryId,
                        DisplayOrder = s.DisplayOrder,
                        Active = s.Active
                    })
                    .ToList()
            })
            .ToListAsync();

        var subcategoriesWithPublishedProducts = (await _context.Products
            .Where(p => p.IsPublished && p.SubcategoryId != null)
            .Select(p => p.SubcategoryId!.Value)
            .Distinct()
            .ToListAsync())
            .ToHashSet();

        var quickFilterSlugs = TShirtFamilySlugs.Concat(PantsFamilySlugs).ToArray();
        // Only the fields quickFiltersBySubSlug (built in the view) actually groups/reads:
        // Subcategory.Slug, Brand.Id/Name, and each attribute value's code+text — no
        // Description/Overview columns, which this query used to pull via plain .Include().
        var quickFilterProducts = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && p.Subcategory != null && quickFilterSlugs.Contains(p.Subcategory.Slug))
            .Select(p => new Product
            {
                Id = p.Id,
                SubcategoryId = p.SubcategoryId,
                Subcategory = p.Subcategory == null ? null : new Subcategory { Id = p.Subcategory.Id, Slug = p.Subcategory.Slug },
                BrandId = p.BrandId,
                Brand = p.Brand == null ? null : new Brand { Id = p.Brand.Id, Name = p.Brand.Name },
                AttributeValues = p.AttributeValues
                    .Where(a => a.Active)
                    .Select(a => new ProductAttributeValue
                    {
                        Id = a.Id,
                        ValueEn = a.ValueEn,
                        AttributeDefinitionId = a.AttributeDefinitionId,
                        AttributeDefinition = a.AttributeDefinition == null
                            ? null
                            : new AttributeDefinition { Id = a.AttributeDefinition.Id, Code = a.AttributeDefinition.Code }
                    })
                    .ToList()
            })
            .ToListAsync();

        var realApparelAttrPairs = (await _context.ProductAttributeValues
            .Where(av => av.Active && av.Product!.IsPublished && av.AttributeDefinition != null && ApparelAttrCodes.Contains(av.AttributeDefinition.Code))
            .Select(av => new { Code = av.AttributeDefinition!.Code, av.ValueEn })
            .Distinct()
            .ToListAsync())
            .Select(x => (x.Code, x.ValueEn))
            .ToHashSet();

        // Collapsed from a per-category N+1 loop (one query per navCategory, every single page
        // load) into one query over every published/in-stock product across all nav categories,
        // grouped and capped to 4 per category in memory. Same "featured first, then newest"
        // order per category as the old loop, and the same lean, no-description shape
        // category-preview-card in the view needs (image, name, price, slug).
        var categoryIds = navCategories.Select(c => c.Id).ToList();
        var categoryPreviewCandidates = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && p.StockQuantity > 0 && categoryIds.Contains(p.CategoryId))
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.Id)
            .Select(p => new Product
            {
                Id = p.Id,
                Name = p.Name,
                NameAr = p.NameAr,
                Slug = p.Slug,
                ImageUrl = p.ImageUrl,
                Price = p.Price,
                CategoryId = p.CategoryId
            })
            .ToListAsync();
        var categoryPreviews = categoryPreviewCandidates
            .GroupBy(p => p.CategoryId)
            .ToDictionary(g => g.Key, g => g.Take(4).ToList());

        var apparelMenuBrands = await _context.Brands
            .AsNoTracking()
            .Where(b => b.Code != null && ApparelMenuBrandCodes.Contains(b.Code))
            .Select(b => new Brand { Id = b.Id, Code = b.Code, Name = b.Name })
            .ToDictionaryAsync(b => b.Code!);

        return new NavMenuData
        {
            NavCategories = navCategories,
            SubcategoriesWithPublishedProducts = subcategoriesWithPublishedProducts,
            QuickFilterProducts = quickFilterProducts,
            RealApparelAttrPairs = realApparelAttrPairs,
            CategoryPreviews = categoryPreviews,
            ApparelMenuBrands = apparelMenuBrands
        };
    }
}
