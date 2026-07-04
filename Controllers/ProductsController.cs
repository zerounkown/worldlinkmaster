using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
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

    public async Task<IActionResult> Index(int? categoryId, string? search, int page = 1)
    {
        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Name.Contains(search) || (p.ShortDescription != null && p.ShortDescription.Contains(search)));
        }

        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var products = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        var vm = new ProductListViewModel
        {
            Products = products,
            Categories = categories,
            SelectedCategoryId = categoryId,
            SearchTerm = search,
            Page = page,
            TotalPages = totalPages
        };

        return View(vm);
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
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
            .Take(4)
            .ToListAsync();

        ViewBag.Related = related;
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        return View(product);
    }
}
