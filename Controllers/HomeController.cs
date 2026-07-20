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
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();

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

    public IActionResult Locations()
    {
        return View();
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
