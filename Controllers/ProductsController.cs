using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Extensions;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class ProductsController : Controller
{
    private const int PageSize = 9;
    private readonly ApplicationDbContext _context;
    private readonly IPromoService _promoService;
    private readonly IProductReviewService _reviewService;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProductsController(ApplicationDbContext context, IPromoService promoService, IProductReviewService reviewService, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _promoService = promoService;
        _reviewService = reviewService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(
        int? categoryId,
        int? subcategoryId, int[]? subcategoryIds,
        int? brandId, int[]? brandIds,
        string[]? colors,
        string[]? sizes,
        int[]? featureIds,
        string[]? attr,
        string[]? availability,
        int? minRating,
        string? search, decimal? minPrice, decimal? maxPrice, string? sort, int page = 1)
    {
        // Tile links and old bookmarks still pass the singular subcategoryId/brandId — fold
        // them into the multi-select lists that the checkbox filters now submit.
        var selectedSubcategoryIds = (subcategoryIds ?? Array.Empty<int>()).ToList();
        if (subcategoryId.HasValue && !selectedSubcategoryIds.Contains(subcategoryId.Value))
        {
            selectedSubcategoryIds.Add(subcategoryId.Value);
        }

        var selectedBrandIds = (brandIds ?? Array.Empty<int>()).ToList();
        if (brandId.HasValue && !selectedBrandIds.Contains(brandId.Value))
        {
            selectedBrandIds.Add(brandId.Value);
        }

        var selectedColors = (colors ?? Array.Empty<string>()).ToList();
        var selectedSizes = (sizes ?? Array.Empty<string>()).ToList();
        var selectedFeatureIds = (featureIds ?? Array.Empty<int>()).ToList();
        var selectedAvailability = (availability ?? Array.Empty<string>()).ToList();

        // Generic attribute filter (mega-menu quick filters — neck type, sleeve length, pattern,
        // etc.): each entry is "Code:Value", e.g. "NECK-TYPE:Crew Neck". Grouped by code so
        // multiple values for the same attribute OR together, same as every other facet dimension.
        var selectedAttributeFilters = (attr ?? Array.Empty<string>())
            .Select(a => a.Split(':', 2))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => (Code: parts[0], Value: parts[1]))
            .GroupBy(f => f.Code)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Value).ToList());

        var baseQuery = _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.Features)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .AsQueryable();

        if (categoryId.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            baseQuery = baseQuery.Where(p =>
                p.Name.Contains(search) ||
                (p.ShortDescription != null && p.ShortDescription.Contains(search)) ||
                (p.NameAr != null && p.NameAr.Contains(search)) ||
                (p.ShortDescriptionAr != null && p.ShortDescriptionAr.Contains(search)));
        }

        // Applies every sidebar facet except whichever one is being counted for its own
        // options — a facet's own counts should reflect "if I also picked this", not shrink
        // to only what's already selected within that same facet.
        IQueryable<Product> ApplyFacets(
            IQueryable<Product> q,
            bool skipSubcategory = false, bool skipBrand = false, bool skipColor = false,
            bool skipSize = false, bool skipFeature = false, bool skipPrice = false,
            bool skipAvailability = false, bool skipRating = false)
        {
            if (!skipSubcategory && selectedSubcategoryIds.Count > 0)
            {
                q = q.Where(p => p.SubcategoryId.HasValue && selectedSubcategoryIds.Contains(p.SubcategoryId.Value));
            }

            if (!skipBrand && selectedBrandIds.Count > 0)
            {
                q = q.Where(p => p.BrandId.HasValue && selectedBrandIds.Contains(p.BrandId.Value));
            }

            if (!skipColor && selectedColors.Count > 0)
            {
                q = q.Where(p => p.Variants.Any(v => v.Color != null && selectedColors.Contains(v.Color.Name)));
            }

            if (!skipSize && selectedSizes.Count > 0)
            {
                q = q.Where(p => p.Variants.Any(v => v.Size != null && selectedSizes.Contains(v.Size.Label)));
            }

            if (!skipFeature && selectedFeatureIds.Count > 0)
            {
                q = q.Where(p => p.Features.Any(f => selectedFeatureIds.Contains(f.Id)));
            }

            foreach (var (code, values) in selectedAttributeFilters)
            {
                q = q.Where(p => p.AttributeValues.Any(av => av.Active && av.AttributeDefinition!.Code == code && values.Contains(av.ValueEn)));
            }

            // Min/max are typed in whichever currency the shopper is currently viewing the
            // store in, so they need converting back to AED before comparing against stored prices.
            if (!skipPrice && minPrice.HasValue)
            {
                var minPriceAed = minPrice.Value.FromDisplayCurrencyToAed();
                q = q.Where(p => p.Price >= minPriceAed);
            }

            if (!skipPrice && maxPrice.HasValue)
            {
                var maxPriceAed = maxPrice.Value.FromDisplayCurrencyToAed();
                q = q.Where(p => p.Price <= maxPriceAed);
            }

            // Both boxes checked (or neither) means no real filtering — either reads as "show everything".
            if (!skipAvailability && selectedAvailability.Count == 1)
            {
                q = selectedAvailability[0] == "in-stock"
                    ? q.Where(p => p.StockQuantity > 0)
                    : q.Where(p => p.StockQuantity == 0);
            }

            if (!skipRating && minRating.HasValue)
            {
                q = q.Where(p => p.Rating >= minRating.Value);
            }

            return q;
        }

        // Slider bounds for the price filter reflect every other active filter so they don't
        // shrink to whatever range the shopper already picked on the price slider itself.
        var priceBoundsQuery = ApplyFacets(baseQuery, skipPrice: true);
        var lowestPrice = await priceBoundsQuery.Select(p => (decimal?)p.Price).MinAsync();
        var highestPrice = await priceBoundsQuery.Select(p => (decimal?)p.Price).MaxAsync();
        var priceRangeMin = lowestPrice.HasValue ? (int)Math.Floor(lowestPrice.Value.ToDisplayCurrencyValue() / 10) * 10 : 0;
        var priceRangeMax = highestPrice.HasValue ? (int)Math.Ceiling(highestPrice.Value.ToDisplayCurrencyValue() / 10) * 10 : 0;

        var query = ApplyFacets(baseQuery);

        query = sort switch
        {
            "price_asc" => query.OrderBy(p => p.Price),
            "price_desc" => query.OrderByDescending(p => p.Price),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            "rating" => query.OrderByDescending(p => p.Rating).ThenByDescending(p => p.ReviewCount),
            _ => query.OrderBy(p => p.Name)
        };

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var products = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var categories = await _context.Categories
            .AsNoTracking()
            .Include(c => c.Products)
            .Include(c => c.Subcategories.OrderBy(s => s.Name))
            .OrderBy(c => c.Name)
            .ToListAsync();
        var allBrands = await _context.Brands.AsNoTracking().ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        // Facet counts — each one applies every filter except its own dimension. Subcategory
        // counts only make sense once a top-level category narrows the field.
        var subcategoryCountsById = new Dictionary<int, int>();
        if (categoryId.HasValue)
        {
            subcategoryCountsById = await ApplyFacets(baseQuery, skipSubcategory: true)
                .Where(p => p.SubcategoryId.HasValue)
                .GroupBy(p => p.SubcategoryId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count);
        }

        var brandFacets = await ApplyFacets(baseQuery, skipBrand: true)
            .Where(p => p.BrandId.HasValue)
            .GroupBy(p => p.BrandId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync();

        var colorFacets = await ApplyFacets(baseQuery, skipColor: true)
            .SelectMany(p => p.Variants.Where(v => v.Color != null), (p, v) => new { p.Id, v.Color!.Name, v.Color.HexCode })
            .GroupBy(x => new { x.Name, x.HexCode })
            .Select(g => new ColorFacetCount { Name = g.Key.Name, HexCode = g.Key.HexCode, Count = g.Select(x => x.Id).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        var sizeFacets = await ApplyFacets(baseQuery, skipSize: true)
            .SelectMany(p => p.Variants.Where(v => v.Size != null), (p, v) => new { p.Id, v.Size!.Label })
            .GroupBy(x => x.Label)
            .Select(g => new LabelFacetCount { Label = g.Key, Count = g.Select(x => x.Id).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        var featureFacets = await ApplyFacets(baseQuery, skipFeature: true)
            .SelectMany(p => p.Features, (p, f) => new { ProductId = p.Id, FeatureId = f.Id, f.Name, f.NameAr })
            .GroupBy(x => new { x.FeatureId, x.Name, x.NameAr })
            .Select(g => new FacetCount { Id = g.Key.FeatureId, Name = g.Key.Name, NameAr = g.Key.NameAr, Count = g.Select(x => x.ProductId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        var availabilityBase = ApplyFacets(baseQuery, skipAvailability: true);
        var inStockCount = await availabilityBase.CountAsync(p => p.StockQuantity > 0);
        var outOfStockCount = await availabilityBase.CountAsync(p => p.StockQuantity == 0);

        var ratingBase = ApplyFacets(baseQuery, skipRating: true);
        var ratingCounts = new Dictionary<int, int>();
        for (var stars = 4; stars >= 1; stars--)
        {
            ratingCounts[stars] = await ratingBase.CountAsync(p => p.Rating >= stars);
        }

        var subcategoryLookup = categories.SelectMany(c => c.Subcategories).ToDictionary(s => s.Id);
        var brandLookup = allBrands.ToDictionary(b => b.Id);

        var vm = new ProductListViewModel
        {
            Products = products,
            Categories = categories,
            Brands = allBrands,
            SelectedCategoryId = categoryId,
            SelectedSubcategoryIds = selectedSubcategoryIds,
            SelectedBrandIds = selectedBrandIds,
            SelectedColors = selectedColors,
            SelectedSizes = selectedSizes,
            SelectedFeatureIds = selectedFeatureIds,
            SelectedAvailability = selectedAvailability,
            MinRating = minRating,
            SearchTerm = search,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            PriceRangeMin = priceRangeMin,
            PriceRangeMax = priceRangeMax,
            SortBy = sort,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount,
            SubcategoryFacets = subcategoryCountsById
                .Where(kvp => subcategoryLookup.ContainsKey(kvp.Key))
                .Select(kvp => new FacetCount { Id = kvp.Key, Name = subcategoryLookup[kvp.Key].Name, NameAr = subcategoryLookup[kvp.Key].NameAr, Count = kvp.Value })
                .OrderByDescending(f => f.Count)
                .ToList(),
            BrandFacets = brandFacets
                .Where(f => brandLookup.ContainsKey(f.Id))
                .Select(f => new FacetCount { Id = f.Id, Name = brandLookup[f.Id].Name, NameAr = brandLookup[f.Id].NameAr, Count = f.Count })
                .OrderByDescending(f => f.Count)
                .ToList(),
            ColorFacets = colorFacets,
            SizeFacets = sizeFacets,
            FeatureFacets = featureFacets,
            InStockCount = inStockCount,
            OutOfStockCount = outOfStockCount,
            RatingCounts = ratingCounts
        };

        return View(vm);
    }

    public async Task<IActionResult> NewArrivals()
    {
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        ViewBag.NewArrivals = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .OrderByDescending(p => p.CreatedAt)
            .Take(6)
            .ToListAsync();

        // "Specials" — the featured lineup, shown at the active promo event's discount when one is running.
        ViewBag.Specials = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .Where(p => p.IsFeatured)
            .OrderByDescending(p => p.Rating)
            .Take(6)
            .ToListAsync();

        return View();
    }

    public async Task<IActionResult> Sales(int page = 1)
    {
        var activeEvent = await _promoService.GetTopActiveEventAsync();
        ViewBag.ActiveEvent = activeEvent;

        // No storewide discount running right now means nothing here is actually "on sale" —
        // show an empty state instead of a plain full-price grid under a "Sales" banner.
        if (activeEvent == null)
        {
            ViewBag.TotalCount = 0;
            return View(new List<Product>());
        }

        var query = _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .Where(p => p.IsFeatured);

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var products = await query
            .OrderByDescending(p => p.Rating)
            .ThenByDescending(p => p.ReviewCount)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        ViewBag.TotalCount = totalCount;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;

        return View(products);
    }

    public async Task<IActionResult> Details(string slug)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Category)
            .Include(p => p.Subcategory)
            .Include(p => p.Brand)
            .Include(p => p.Merchant)
            .Include(p => p.Images.OrderBy(i => i.SortOrder))
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder)).ThenInclude(pc => pc.Color)
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .Include(p => p.Features)
            .Include(p => p.AttributeValues.Where(a => a.Active).OrderBy(a => a.DisplayOrder)).ThenInclude(a => a.AttributeDefinition)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Slug == slug);

        if (product == null)
        {
            return NotFound();
        }

        var related = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished)
            .Include(p => p.Category)
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
            .Take(4)
            .ToListAsync();

        ViewBag.Related = related;
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();
        ViewBag.Reviews = await _reviewService.GetReviewsAsync(product.Id);

        var currentUserId = _userManager.GetUserId(User);
        ViewBag.MyReview = currentUserId != null ? await _reviewService.GetUserReviewAsync(product.Id, currentUserId) : null;

        return View(product);
    }
}
