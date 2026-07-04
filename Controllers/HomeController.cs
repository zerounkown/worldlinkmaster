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
            .Where(p => p.IsFeatured)
            .OrderBy(p => p.Name)
            .Take(8)
            .ToListAsync();

        return View(featured);
    }

    public IActionResult About()
    {
        return View();
    }

    public IActionResult Contact()
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
