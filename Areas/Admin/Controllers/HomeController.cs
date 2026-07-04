using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class HomeController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.ProductCount = await _context.Products.CountAsync();
        ViewBag.CategoryCount = await _context.Categories.CountAsync();
        ViewBag.OrderCount = await _context.Orders.CountAsync();
        ViewBag.RecentOrders = await _context.Orders
            .OrderByDescending(o => o.OrderDate)
            .Take(5)
            .ToListAsync();

        return View();
    }
}
