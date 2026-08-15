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
    private readonly IGeocodingService _geocodingService;

    public HomeController(ApplicationDbContext context, IPromoService promoService, IGeocodingService geocodingService)
    {
        _context = context;
        _promoService = promoService;
        _geocodingService = geocodingService;
    }

    // "/" is served by Welcome (see Program.cs "root" route). This action used to render a
    // second, separately-maintained homepage at /Home/Index that nothing in the site linked to —
    // it had drifted out of sync with Welcome (different footer copy, different product rows).
    // Rather than keep two homepages in sync forever, /Home/Index now just forwards to the one
    // real homepage so old bookmarks/links still land somewhere correct.
    public IActionResult Index()
    {
        return RedirectToAction(nameof(Welcome));
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
        ViewBag.HomeBanners = await _context.HomeBanners
            .Where(b => b.Active)
            .OrderBy(b => b.DisplayOrder)
            .ToListAsync();
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
        // Only show brands with a real logo in the "Shop by Brand" row — mixing those in
        // with plain text-wordmark placeholder tiles looked inconsistent.
        ViewBag.Brands = await _context.Brands.Where(b => b.LogoUrl != null)
            .OrderByDescending(b => b.Name == "WLM")
            .ThenBy(b => b.Name)
            .ToListAsync();
        ViewBag.ActiveEvent = await _promoService.GetTopActiveEventAsync();

        // "Best sellers" — approximated by review volume/rating since there's no sales-count field yet.
        ViewBag.BestSellers = await _context.Products
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .OrderByDescending(p => p.ReviewCount)
            .ThenByDescending(p => p.Rating)
            .Take(6)
            .ToListAsync();

        ViewBag.NewArrivals = await _context.Products
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Include(p => p.Variants).ThenInclude(v => v.ProductColor)
            .OrderByDescending(p => p.CreatedAt)
            .Take(6)
            .ToListAsync();

        return View();
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Contact()
    {
        return View(new WorldLinkMaster.Web.Models.ViewModels.LeadFormViewModel());
    }

    public async Task<IActionResult> Locations()
    {
        var stores = await _context.Stores
            .Where(s => s.Active)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync();
        return View(stores);
    }

    // Shared geocoding proxy for the Find a Store search box and the admin Stores
    // "Autofill from address" button — see IGeocodingService for why this is server-side.
    [HttpGet]
    public async Task<IActionResult> GeocodeSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Json(new { success = false, message = "Enter an address to search." });
        }

        var result = await _geocodingService.GeocodeAsync(query);
        if (result == null)
        {
            return Json(new { success = false, message = "Couldn't find that address." });
        }

        return Json(new { success = true, lat = result.Lat, lng = result.Lng, displayName = result.DisplayName });
    }

    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Terms()
    {
        return View();
    }

    public IActionResult ReturnPolicy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
