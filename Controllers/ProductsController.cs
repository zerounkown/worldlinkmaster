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

    public ProductsController(ApplicationDbContext context, IPromoService promoService)
    {
        _context = context;
        _promoService = promoService;
    }

    public async Task<IActionResult> Index(int? categoryId, int? subcategoryId, string? search, decimal? minPrice, decimal? maxPrice, string? sort, int page = 1)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (subcategoryId.HasValue)
        {
            query = query.Where(p => p.SubcategoryId == subcategoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p =>
                p.Name.Contains(search) ||
                (p.ShortDescription != null && p.ShortDescription.Contains(search)) ||
                (p.NameAr != null && p.NameAr.Contains(search)) ||
                (p.ShortDescriptionAr != null && p.ShortDescriptionAr.Contains(search)));
        }

        // Min/max are typed in whichever currency the shopper is currently viewing the store
        // in, so they need converting back to AED before comparing against stored prices.
        if (minPrice.HasValue)
        {
            var minPriceAed = minPrice.Value.FromDisplayCurrencyToAed();
            query = query.Where(p => p.Price >= minPriceAed);
        }

        if (maxPrice.HasValue)
        {
            var maxPriceAed = maxPrice.Value.FromDisplayCurrencyToAed();
            query = query.Where(p => p.Price <= maxPriceAed);
        }

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
            .Include(c => c.Products)
            .Include(c => c.Subcategories.OrderBy(s => s.Name))
            .OrderBy(c => c.Name)
            .ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        // "Best sellers" — approximated by review volume/rating since there's no sales-count field yet.
        ViewBag.BestSellers = await _context.Products
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .OrderByDescending(p => p.ReviewCount)
            .ThenByDescending(p => p.Rating)
            .Take(8)
            .ToListAsync();

        var vm = new ProductListViewModel
        {
            Products = products,
            Categories = categories,
            SelectedCategoryId = categoryId,
            SelectedSubcategoryId = subcategoryId,
            SearchTerm = search,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            SortBy = sort,
            Page = page,
            TotalPages = totalPages,
            TotalCount = totalCount
        };

        return View(vm);
    }

    public async Task<IActionResult> NewArrivals()
    {
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        ViewBag.NewArrivals = await _context.Products
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .OrderByDescending(p => p.CreatedAt)
            .Take(6)
            .ToListAsync();

        // "Specials" — the featured lineup, shown at the active promo event's discount when one is running.
        ViewBag.Specials = await _context.Products
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
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
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
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
            .Include(p => p.Category)
            .Include(p => p.Merchant)
            .Include(p => p.Images.OrderBy(i => i.SortOrder))
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(p => p.Slug == slug);

        if (product == null)
        {
            return NotFound();
        }

        var related = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
            .Take(4)
            .ToListAsync();

        ViewBag.Related = related;
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        return View(product);
    }
}
