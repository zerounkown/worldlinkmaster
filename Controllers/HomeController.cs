using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPromoService _promoService;

    public HomeController(ApplicationDbContext context, IPromoService promoService)
    {
        _context = context;
        _promoService = promoService;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();
        ViewBag.ShowcaseEvents = await _promoService.GetShowcaseEventsAsync();
        var featured = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .Where(p => p.IsFeatured)
            .OrderBy(p => p.Name)
            .Take(8)
            .ToListAsync();
        var featuredIds = featured.Select(p => p.Id).ToList();

        // "Best sellers" — approximated by review volume/rating since there's no sales-count field yet.
        ViewBag.BestSellers = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .Where(p => !featuredIds.Contains(p.Id))
            .OrderByDescending(p => p.ReviewCount)
            .ThenByDescending(p => p.Rating)
            .Take(4)
            .ToListAsync();
        var bestSellerIds = ((List<Product>)ViewBag.BestSellers).Select(p => p.Id).ToList();

        // "Trending / variety" — a different slice (most recently added) so the homepage shows
        // a broader assortment than just featured + best-sellers.
        ViewBag.TrendingProducts = await _context.Products
            .Include(p => p.Category)
            .Include(p => p.Colors.OrderBy(c => c.SortOrder))
            .Include(p => p.Sizes.OrderBy(s => s.SortOrder))
            .Where(p => !featuredIds.Contains(p.Id) && !bestSellerIds.Contains(p.Id))
            .OrderByDescending(p => p.CreatedAt)
            .Take(8)
            .ToListAsync();

        return View(featured);
    }

    public async Task<IActionResult> Welcome()
    {
        // Feeds the auto-scrolling "What We Sell" product strip — featured items first, topped
        // up with the newest arrivals so the strip always has enough items for a smooth loop.
        var scrollProducts = await _context.Products
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.CreatedAt)
            .Take(16)
            .ToListAsync();
        ViewBag.ScrollProducts = scrollProducts;
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();

        return View();
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Contact()
    {
        return View();
    }

    public IActionResult Locations()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
