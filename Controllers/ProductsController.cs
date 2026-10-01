using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Extensions;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class ProductsController : Controller
{
    private const int PageSize = 20;
    private readonly ApplicationDbContext _context;
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly IPromoService _promoService;
    private readonly IProductReviewService _reviewService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStorefrontCacheService _storefrontCache;

    public ProductsController(ApplicationDbContext context, IDbContextFactory<ApplicationDbContext> contextFactory, IPromoService promoService, IProductReviewService reviewService, UserManager<ApplicationUser> userManager, IStorefrontCacheService storefrontCache)
    {
        _context = context;
        _contextFactory = contextFactory;
        _promoService = promoService;
        _reviewService = reviewService;
        _userManager = userManager;
        _storefrontCache = storefrontCache;
    }

    // _ProductCard.cshtml (and the extension methods it calls — GetHoverImageUrl, LocalizedName)
    // only ever reads the fields projected below, across the whole catalog-grid/related-products
    // surface this feeds (listing page, related products on the PDP). The plain .Include()-based
    // materialization this replaces pulled every column on Product, including Description/
    // DescriptionAr/ShortDescription/ShortDescriptionAr/Overview/OverviewAr (up to ~12KB of text
    // per row) for data the card never displays. Applied after Skip/Take (paging already
    // happened), so this only affects the page of rows actually being rendered, not the
    // filter/sort/count queries built on IQueryable<Product> upstream of it.
    private static IQueryable<Product> ProjectForCard(IQueryable<Product> source) =>
        source.Select(p => new Product
        {
            Id = p.Id,
            Name = p.Name,
            NameAr = p.NameAr,
            Slug = p.Slug,
            Price = p.Price,
            StockQuantity = p.StockQuantity,
            ImageUrl = p.ImageUrl,
            IsFeatured = p.IsFeatured,
            Variants = p.Variants.Select(v => new ProductVariant
            {
                Id = v.Id,
                ColorId = v.ColorId,
                Color = v.Color == null ? null : new Color { Id = v.Color.Id, Name = v.Color.Name, HexCode = v.Color.HexCode, FamilyId = v.Color.FamilyId },
                SizeId = v.SizeId,
                Size = v.Size == null ? null : new Size { Id = v.Size.Id, Label = v.Size.Label },
                ProductColorId = v.ProductColorId,
                ProductColor = v.ProductColor == null ? null : new ProductColor { Id = v.ProductColor.Id, SwatchImageUrl = v.ProductColor.SwatchImageUrl },
                Price = v.Price,
                StockQuantity = v.StockQuantity,
                Active = v.Active,
                ImageUrl = v.ImageUrl
            }).ToList(),
            ProductColors = p.ProductColors
                .Where(pc => pc.Active)
                .OrderBy(pc => pc.DisplayOrder)
                .Select(pc => new ProductColor
                {
                    Id = pc.Id,
                    ColorId = pc.ColorId,
                    DefaultColor = pc.DefaultColor,
                    SwatchImageUrl = pc.SwatchImageUrl,
                    Active = pc.Active
                }).ToList(),
            Media = p.Media
                .Where(m => m.Active)
                .OrderBy(m => m.DisplayOrder)
                .Select(m => new ProductMedia
                {
                    Id = m.Id,
                    ProductColorId = m.ProductColorId,
                    MediaType = m.MediaType,
                    MediaUrl = m.MediaUrl,
                    DisplayOrder = m.DisplayOrder,
                    ShowInGallery = m.ShowInGallery,
                    Active = m.Active
                }).ToList()
        });

    [OutputCache(PolicyName = "ProductListing")]
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

        // "colors" carries color-FAMILY codes (e.g. "black", "tan-coyote"), not individual color
        // names — the listing-page filter facets on families only; PDP/product-card swatches are
        // untouched and keep showing the specific vendor color.
        var selectedColorFamilyCodes = (colors ?? Array.Empty<string>()).ToList();
        var allColorFamilies = await _storefrontCache.GetColorFamiliesAsync();
        var colorFamilyByCode = allColorFamilies.ToDictionary(f => f.Code);
        // -1 is a safe placeholder if the "Other" family row is somehow missing (e.g. the
        // ColorFamilies seed hasn't run yet) — it can never equal a real FamilyId, so unmapped
        // colors just don't match any selected family filter instead of crashing the whole page.
        var otherFamilyId = colorFamilyByCode.TryGetValue("other", out var otherFamily) ? otherFamily.Id : -1;
        var selectedFamilyIds = selectedColorFamilyCodes
            .Where(c => colorFamilyByCode.ContainsKey(c))
            .Select(c => colorFamilyByCode[c].Id)
            .ToList();
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

        var searchTrimmed = search?.Trim() ?? string.Empty;
        var searchIsNumericId = int.TryParse(searchTrimmed, out var searchNumericId);

        // An exact (case-insensitive) match on a variant's own Sku or Internal Barcode is
        // unambiguous — go straight to that variant's product page instead of a results page
        // that would just show a list of one. Deliberately scoped to variant fields only (not
        // the product's own Sku): a product-level Sku match is still "the product," but a
        // variant-level exact match is "this exact color+size," which is worth landing on
        // directly with that variant pre-selected (see Details.cshtml's ?variant= handling).
        // Partial matches (the common case while typing) are unaffected — this only fires when
        // the whole trimmed search text equals a variant's Sku/Barcode outright.
        if (!string.IsNullOrWhiteSpace(searchTrimmed))
        {
            var exactVariantMatch = await FindExactVariantMatchAsync(searchTrimmed);
            if (exactVariantMatch != null)
            {
                return RedirectToAction(nameof(Details), new { slug = exactVariantMatch.Product!.Slug, variant = exactVariantMatch.Id });
            }
        }

        // Factored out (rather than a plain local variable) so the facet-count queries below can
        // rebuild the same filtered base query against their own separate DbContext instances —
        // needed to actually run them concurrently, since a single context can't handle more than
        // one in-flight query at a time.
        IQueryable<Product> BuildBaseQuery(ApplicationDbContext ctx)
        {
            var q = ctx.Products
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
                q = q.Where(p => p.CategoryId == categoryId.Value);
            }

            // Multi-field search: SKU / numeric product ID / variant barcode (identifier fields,
            // partial + case-insensitive via ILIKE, same as everything else here) / name / brand /
            // variant color / description, all in one query rather than name-only. No Nickname or
            // Barcode-on-Product field exists in the schema, so those two are out of scope here —
            // see the PR description for what that would take to add.
            if (!string.IsNullOrWhiteSpace(searchTrimmed))
            {
                q = q.Where(p =>
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

            return q;
        }

        var baseQuery = BuildBaseQuery(_context);

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

            if (!skipColor && selectedFamilyIds.Count > 0)
            {
                q = q.Where(p => p.Variants.Any(v => v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId)));
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
            // store in, so they need converting back to AED before comparing against stored
            // prices — see ApplyVariantPriceFilter below for the actual (variant-aware) filter.
            if (!skipPrice && (minPrice.HasValue || maxPrice.HasValue))
            {
                q = ApplyVariantPriceFilter(
                    q,
                    minPrice.HasValue ? minPrice.Value.FromDisplayCurrencyToAed() : null,
                    maxPrice.HasValue ? maxPrice.Value.FromDisplayCurrencyToAed() : null,
                    selectedFamilyIds,
                    otherFamilyId);
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
        // shrink to whatever range the shopper already picked on the price slider itself. Also
        // variant-price-aware, same scoping as the filter above and the same one-value-per-
        // product shape as price sorting below: each product contributes the min/max among its
        // own in-scope variants (falling back to its own Price when it has none), not a
        // SelectMany-joined row per variant — that would have meant re-running priceBoundsQuery's
        // own filters (and every Include behind it) a second time as a UNIONed subquery, which is
        // exactly the kind of extra round trip this needs to avoid.
        // (decimal?) on the OUTER Select, not just the inner one, so MinAsync()/MaxAsync() return
        // null gracefully (instead of throwing) when the current filters match zero products —
        // same reason the original, simpler version of this query cast to decimal? too.
        var priceBoundsQuery = ApplyFacets(baseQuery, skipPrice: true);
        var lowestPrice = await priceBoundsQuery
            .Select(p => (decimal?)(p.Variants
                .Where(v => selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId)))
                .Select(v => (decimal?)(v.Price ?? p.Price))
                .Min() ?? p.Price))
            .MinAsync();
        var highestPrice = await priceBoundsQuery
            .Select(p => (decimal?)(p.Variants
                .Where(v => selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId)))
                .Select(v => (decimal?)(v.Price ?? p.Price))
                .Max() ?? p.Price))
            .MaxAsync();
        var priceRangeMin = lowestPrice.HasValue ? (int)Math.Floor(lowestPrice.Value.ToDisplayCurrencyValue() / 10) * 10 : 0;
        var priceRangeMax = highestPrice.HasValue ? (int)Math.Ceiling(highestPrice.Value.ToDisplayCurrencyValue() / 10) * 10 : 0;

        var query = ApplyFacets(baseQuery);

        // Relevance ranking only kicks in for the default sort while a search is active — an
        // explicit sort choice (price/newest/rating) still wins, same as Amazon/Propper letting
        // you re-sort search results. Tiers: exact identifier match (SKU/ID/barcode) > exact or
        // prefix name match > partial name match > matched only in description/brand/color.
        // Price sorting uses the same effective price _ProductCard.cshtml shows on the card: the
        // cheapest in-scope variant (scoped to the selected colors, same as the filter/bounds
        // above), falling back to the product's own Price when it has no variants at all.
        query = sort switch
        {
            "price_asc" => query.OrderBy(p => p.Variants
                .Where(v => selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId)))
                .Select(v => (decimal?)(v.Price ?? p.Price))
                .Min() ?? p.Price),
            "price_desc" => query.OrderByDescending(p => p.Variants
                .Where(v => selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId)))
                .Select(v => (decimal?)(v.Price ?? p.Price))
                .Min() ?? p.Price),
            "newest" => query.OrderByDescending(p => p.CreatedAt),
            "rating" => query.OrderByDescending(p => p.Rating).ThenByDescending(p => p.ReviewCount),
            _ when !string.IsNullOrWhiteSpace(searchTrimmed) => query
                .OrderByDescending(p =>
                    (EF.Functions.ILike(p.Sku, searchTrimmed)
                        || (searchIsNumericId && p.Id == searchNumericId)
                        || p.Variants.Any(v => EF.Functions.ILike(v.Sku, searchTrimmed))
                        || p.Variants.Any(v => v.Barcode != null && EF.Functions.ILike(v.Barcode, searchTrimmed)))
                        ? 100
                    : (EF.Functions.ILike(p.Name, searchTrimmed) || EF.Functions.ILike(p.Name, searchTrimmed + "%"))
                        ? 80
                    : EF.Functions.ILike(p.Name, "%" + searchTrimmed + "%")
                        ? 60
                    : 40)
                .ThenBy(p => p.Name),
            _ => query.OrderBy(p => p.Name)
        };

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var products = await ProjectForCard(query
            .Skip((page - 1) * PageSize)
            .Take(PageSize))
            .ToListAsync();

        // Views/Products/Index.cshtml never reads Category.Products or a product count derived
        // from it (only .Subcategories) — that Include used to hydrate every published Product
        // row for every category on every listing/search request for nothing.
        var categories = await _context.Categories
            .AsNoTracking()
            .Include(c => c.Subcategories.OrderBy(s => s.Name))
            .OrderBy(c => c.Name)
            .ToListAsync();
        var allBrands = await _context.Brands.AsNoTracking().ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        // Facet counts — each one applies every filter except its own dimension. This used to run
        // 7 separate DbContexts concurrently (a single context can't have more than one query in
        // flight, so true parallelism needed separate connections) — 8 concurrent Postgres
        // connections per listing-page hit once the base query is counted too. That fan-out was
        // identified as a major contributor to Supabase connection-pool exhaustion under load, so
        // it's consolidated into 2 batches sharing one DbContext each: queries within a batch run
        // sequentially (same pattern already used below for availability/rating), and the 2
        // batches run concurrently against each other. Same facet queries, same results — 3
        // connections per request (base + 2 batches) instead of 8.
        async Task<(Dictionary<int, int> SubcategoryCounts, List<(int Id, int Count)> Brands, (int InStock, int OutOfStock) Availability, Dictionary<int, int> Rating)> RunCountFacetsAsync()
        {
            await using var facetContext = await _contextFactory.CreateDbContextAsync();
            var bq = BuildBaseQuery(facetContext);

            // Subcategory counts only make sense once a top-level category narrows the field.
            var subcategoryCounts = categoryId.HasValue
                ? await ApplyFacets(bq, skipSubcategory: true)
                    .Where(p => p.SubcategoryId.HasValue)
                    .GroupBy(p => p.SubcategoryId!.Value)
                    .Select(g => new { Id = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Id, x => x.Count)
                : new Dictionary<int, int>();

            var brands = await ApplyFacets(bq, skipBrand: true)
                .Where(p => p.BrandId.HasValue)
                .GroupBy(p => p.BrandId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToListAsync();

            var availabilityBase = ApplyFacets(bq, skipAvailability: true);
            var inStock = await availabilityBase.CountAsync(p => p.StockQuantity > 0);
            var outOfStock = await availabilityBase.CountAsync(p => p.StockQuantity == 0);

            var ratingBase = ApplyFacets(bq, skipRating: true);
            var ratingCounts = new Dictionary<int, int>();
            for (var stars = 4; stars >= 1; stars--)
            {
                ratingCounts[stars] = await ratingBase.CountAsync(p => p.Rating >= stars);
            }

            return (subcategoryCounts, brands.Select(x => (x.Id, x.Count)).ToList(), (inStock, outOfStock), ratingCounts);
        }

        async Task<(List<ColorFamilyFacetCount> Colors, List<LabelFacetCount> Sizes, List<FacetCount> Features)> RunVariantFacetsAsync()
        {
            await using var facetContext = await _contextFactory.CreateDbContextAsync();
            var bq = BuildBaseQuery(facetContext);

            var colorFamilyCounts = await ApplyFacets(bq, skipColor: true)
                .SelectMany(p => p.Variants.Where(v => v.Color != null), (p, v) => new { p.Id, FamilyId = v.Color!.FamilyId ?? otherFamilyId })
                .GroupBy(x => x.FamilyId)
                .Select(g => new { FamilyId = g.Key, Count = g.Select(x => x.Id).Distinct().Count() })
                .ToListAsync();

            // Families with 0 matching products (given the currently-active filters) are simply
            // absent from colorFamilyCounts — nothing extra needed to "hide" them.
            var colors = colorFamilyCounts
                .Where(c => colorFamilyByCode.Values.Any(f => f.Id == c.FamilyId))
                .Select(c =>
                {
                    var f = allColorFamilies.First(x => x.Id == c.FamilyId);
                    return new ColorFamilyFacetCount
                    {
                        Id = f.Id,
                        Code = f.Code,
                        Name = f.Name,
                        NameAr = f.NameAr,
                        HexCode = f.HexCode,
                        SwatchImageUrl = f.SwatchImageUrl,
                        Count = c.Count
                    };
                })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name)
                .ToList();

            var sizes = await ApplyFacets(bq, skipSize: true)
                .SelectMany(p => p.Variants.Where(v => v.Size != null), (p, v) => new { p.Id, v.Size!.Label })
                .GroupBy(x => x.Label)
                .Select(g => new LabelFacetCount { Label = g.Key, Count = g.Select(x => x.Id).Distinct().Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Label)
                .ToListAsync();

            var features = await ApplyFacets(bq, skipFeature: true)
                .SelectMany(p => p.Features, (p, f) => new { ProductId = p.Id, FeatureId = f.Id, f.Name, f.NameAr })
                .GroupBy(x => new { x.FeatureId, x.Name, x.NameAr })
                .Select(g => new FacetCount { Id = g.Key.FeatureId, Name = g.Key.Name, NameAr = g.Key.NameAr, Count = g.Select(x => x.ProductId).Distinct().Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Name)
                .ToListAsync();

            return (colors, sizes, features);
        }

        var countFacetsTask = RunCountFacetsAsync();
        var variantFacetsTask = RunVariantFacetsAsync();
        await Task.WhenAll(countFacetsTask, variantFacetsTask);

        var countFacets = await countFacetsTask;
        var variantFacets = await variantFacetsTask;

        var subcategoryCountsById = countFacets.SubcategoryCounts;
        var brandFacets = countFacets.Brands;
        var (inStockCount, outOfStockCount) = countFacets.Availability;
        var ratingCounts = countFacets.Rating;

        var colorFacets = variantFacets.Colors;
        var sizeFacets = variantFacets.Sizes;
        var featureFacets = variantFacets.Features;

        var subcategoryLookup = categories.SelectMany(c => c.Subcategories).ToDictionary(s => s.Id);
        var brandLookup = allBrands.ToDictionary(b => b.Id);

        // Distinct from "0 results under the active filters" — this specifically flags a
        // subcategory that has zero published products at all, so the view can show a friendly
        // "nothing here yet" message with a link back to the category instead of the generic
        // no-results text.
        var selectedSubcategoryIsEmpty = selectedSubcategoryIds.Count == 1
            && !await _context.Products.AnyAsync(p => p.SubcategoryId == selectedSubcategoryIds[0] && p.IsPublished);

        var vm = new ProductListViewModel
        {
            Products = products,
            Categories = categories,
            Brands = allBrands,
            SelectedCategoryId = categoryId,
            SelectedSubcategoryIds = selectedSubcategoryIds,
            SelectedSubcategoryIsEmpty = selectedSubcategoryIsEmpty,
            SelectedBrandIds = selectedBrandIds,
            SelectedColorFamilies = selectedColorFamilyCodes,
            SelectedColorFamilyIds = selectedFamilyIds,
            OtherColorFamilyId = otherFamilyId,
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
            ColorFamilyFacets = colorFacets,
            SizeFacets = sizeFacets,
            FeatureFacets = featureFacets,
            InStockCount = inStockCount,
            OutOfStockCount = outOfStockCount,
            RatingCounts = ratingCounts
        };

        return View(vm);
    }

    // A product matches if AT LEAST ONE of its variants — scoped to the selected colors, exactly
    // like the color filter above, when any are selected — has an effective price (its own
    // Price, falling back to the product's base Price exactly like everywhere else in the app)
    // inside the range. Products with no variants at all fall back to their own Price directly.
    // This is what makes e.g. a product priced 530.25 everywhere except one color at 472.50
    // filterable/findable by that one color's real price, not just the product's base price.
    //
    // internal (not a local function inside Index()) purely so Tests/UnitTests can call it
    // directly against a SQLite-backed context: Index() itself can't run there at all — it
    // unconditionally computes price-range slider bounds via MinAsync()/MaxAsync() over a
    // decimal column, which SQLite's EF Core provider can't translate (see
    // ProductsControllerTests.IndexSkipReason) — but this filter is plain WHERE-clause decimal
    // comparisons, no aggregates, which both providers handle identically.
    internal static IQueryable<Product> ApplyVariantPriceFilter(IQueryable<Product> query, decimal? minPriceAed, decimal? maxPriceAed, List<int> selectedFamilyIds, int otherFamilyId)
    {
        if (minPriceAed.HasValue)
        {
            var min = minPriceAed.Value;
            query = query.Where(p =>
                p.Variants.Any(v => (selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId))) && (v.Price ?? p.Price) >= min)
                || (!p.Variants.Any() && p.Price >= min));
        }

        if (maxPriceAed.HasValue)
        {
            var max = maxPriceAed.Value;
            query = query.Where(p =>
                p.Variants.Any(v => (selectedFamilyIds.Count == 0 || (v.Color != null && selectedFamilyIds.Contains(v.Color.FamilyId ?? otherFamilyId))) && (v.Price ?? p.Price) <= max)
                || (!p.Variants.Any() && p.Price <= max));
        }

        return query;
    }

    // Header instant-search dropdown. One lightweight query (no N+1: the matching variant, if
    // any, is a correlated subquery projected alongside each product, not a separate round
    // trip), capped to InstantSearchMaxResults and gated to a 2+ character term server-side too
    // (not just the debounced client) so a stray 1-character request can't scan the table.
    // ILIKE + the trigram indexes in ApplicationDbContext power this on Postgres (production);
    // SQLite (fast unit tests) can't translate ILIKE at all, so it gets an equivalent
    // ToLower().Contains() query instead — same results, no index, which is fine at unit-test
    // data volumes. Some imported NameAr values carry a stray U+200E (LEFT-TO-RIGHT MARK)
    // character right before a run of digits (e.g. "...200E24-7"), which would otherwise split
    // "24-7" across the invisible character and silently fail to match — stripped out of NameAr
    // before comparing, on both provider paths.
    private const int InstantSearchMaxResults = 8;
    private const string LeftToRightMark = "‎";

    [HttpGet]
    public async Task<IActionResult> InstantSearch(string? q)
    {
        var term = (q ?? string.Empty).Trim();
        if (term.Length < 2)
        {
            return Json(new { results = Array.Empty<object>() });
        }

        var candidates = _context.Database.IsNpgsql()
            ? await InstantSearchNpgsqlAsync(term)
            : await InstantSearchSqliteAsync(term);

        var isArabicUi = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";
        var payload = candidates.Select(c => new
        {
            slug = c.Slug,
            name = isArabicUi && !string.IsNullOrEmpty(c.NameAr) ? c.NameAr : c.Name,
            image = ImagePlaceholder.IsRealImageUrl(c.ImageUrl) ? c.ImageUrl : ImagePlaceholder.DataUri,
            price = c.Price.ToDisplayCurrency(),
            // MatchedVariantId (set only when this product matched via a variant's Sku/Barcode)
            // carries color AND size through in one URL param, via Details.cshtml's ?variant=
            // handling — a plain product/name match has no specific variant, so null here means
            // no ?variant= at all and the page opens on its own normal default color+size.
            url = Url.Action("Details", "Products", new { slug = c.Slug, variant = c.MatchedVariantId })
        });

        return Json(new
        {
            results = payload,
            searchUrl = Url.Action("Index", "Products", new { search = term })
        });
    }

    // Exact (case-insensitive) match on a variant's own Sku or Barcode — used by Index to
    // redirect straight to the product (see there) and shared here so InstantSearch's dropdown
    // and the full-page redirect agree on exactly what counts as "exact." Deliberately not
    // provider-branched like InstantSearchNpgsqlAsync/SqliteAsync below: plain equality (not a
    // %wildcard% pattern) translates identically on Postgres and SQLite, so there's no need for
    // an ILike vs ToLower().Contains() split here.
    private async Task<ProductVariant?> FindExactVariantMatchAsync(string term)
    {
        var lowerTerm = term.ToLowerInvariant();
        return await _context.ProductVariants
            .AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.Product!.IsPublished && (v.Sku.ToLower() == lowerTerm || (v.Barcode != null && v.Barcode.ToLower() == lowerTerm)))
            .FirstOrDefaultAsync();
    }

    private record InstantSearchCandidate(string Slug, string Name, string? NameAr, decimal Price, string? ImageUrl, int? MatchedVariantId);

    private async Task<List<InstantSearchCandidate>> InstantSearchNpgsqlAsync(string term)
    {
        var pattern = $"%{term}%";
        var lowerTerm = term.ToLowerInvariant();

        return await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && (
                EF.Functions.ILike(p.Sku, pattern) ||
                EF.Functions.ILike(p.Name, pattern) ||
                (p.NameAr != null && EF.Functions.ILike(p.NameAr.Replace(LeftToRightMark, ""), pattern)) ||
                p.Variants.Any(v => EF.Functions.ILike(v.Sku, pattern)) ||
                p.Variants.Any(v => v.Barcode != null && EF.Functions.ILike(v.Barcode, pattern))))
            .OrderByDescending(p => p.Sku.ToLower() == lowerTerm ? 3 : (p.Name.ToLower().StartsWith(lowerTerm) ? 2 : 1))
            .ThenBy(p => p.Name)
            .Take(InstantSearchMaxResults)
            .Select(p => new InstantSearchCandidate(
                p.Slug,
                p.Name,
                p.NameAr,
                p.Price,
                p.ImageUrl,
                p.Variants
                    .Where(v => EF.Functions.ILike(v.Sku, pattern) || (v.Barcode != null && EF.Functions.ILike(v.Barcode, pattern)))
                    .Select(v => (int?)v.Id)
                    .FirstOrDefault()))
            .ToListAsync();
    }

    private async Task<List<InstantSearchCandidate>> InstantSearchSqliteAsync(string term)
    {
        var lowerTerm = term.ToLowerInvariant();

        return await _context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && (
                p.Sku.ToLower().Contains(lowerTerm) ||
                p.Name.ToLower().Contains(lowerTerm) ||
                (p.NameAr != null && p.NameAr.Replace(LeftToRightMark, "").ToLower().Contains(lowerTerm)) ||
                p.Variants.Any(v => v.Sku.ToLower().Contains(lowerTerm)) ||
                p.Variants.Any(v => v.Barcode != null && v.Barcode.ToLower().Contains(lowerTerm))))
            .OrderByDescending(p => p.Sku.ToLower() == lowerTerm ? 3 : (p.Name.ToLower().StartsWith(lowerTerm) ? 2 : 1))
            .ThenBy(p => p.Name)
            .Take(InstantSearchMaxResults)
            .Select(p => new InstantSearchCandidate(
                p.Slug,
                p.Name,
                p.NameAr,
                p.Price,
                p.ImageUrl,
                p.Variants
                    .Where(v => v.Sku.ToLower().Contains(lowerTerm) || (v.Barcode != null && v.Barcode.ToLower().Contains(lowerTerm)))
                    .Select(v => (int?)v.Id)
                    .FirstOrDefault()))
            .ToListAsync();
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

    [OutputCache(PolicyName = "ProductDetail")]
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
            .Include(p => p.Variants).ThenInclude(v => v.Size).ThenInclude(s => s!.SizeGroup)
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

        var related = await ProjectForCard(_context.Products
            .AsNoTracking()
            .Where(p => p.IsPublished && p.CategoryId == product.CategoryId && p.Id != product.Id)
            .Take(4))
            .ToListAsync();

        ViewBag.Related = related;
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();
        ViewBag.Reviews = await _reviewService.GetReviewsAsync(product.Id);

        var currentUserId = _userManager.GetUserId(User);
        ViewBag.MyReview = currentUserId != null ? await _reviewService.GetUserReviewAsync(product.Id, currentUserId) : null;

        return View(product);
    }
}
