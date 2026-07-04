using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Merchant.Controllers;

public class OrdersController : MerchantBaseController
{
    public OrdersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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

        var items = await Context.OrderItems
            .Include(oi => oi.Order)
            .Where(oi => oi.MerchantId == merchant.Id)
            .OrderByDescending(oi => oi.Order!.OrderDate)
            .ToListAsync();

        var payouts = await Context.MerchantPayouts
            .Where(p => p.MerchantId == merchant.Id)
            .ToListAsync();

        ViewBag.Payouts = payouts.ToDictionary(p => p.OrderId, p => p);

        return View(items);
    }
}
