using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Merchant.Controllers;

public class HomeController : MerchantBaseController
{
    public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        : base(context, userManager)
    {
    }

    public async Task<IActionResult> Index()
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        ViewBag.Merchant = merchant;
        ViewBag.ProductCount = await Context.Products.CountAsync(p => p.MerchantId == merchant.Id);

        var items = await Context.OrderItems
            .Where(oi => oi.MerchantId == merchant.Id)
            .Include(oi => oi.Order)
            .ToListAsync();

        ViewBag.OrderCount = items.Select(i => i.OrderId).Distinct().Count();
        ViewBag.TotalSales = items.Sum(i => i.LineTotal);

        var payouts = await Context.MerchantPayouts
            .Where(p => p.MerchantId == merchant.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .ToListAsync();
        ViewBag.RecentPayouts = payouts;

        return View();
    }
}
