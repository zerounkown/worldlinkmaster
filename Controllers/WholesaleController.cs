using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class WholesaleController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public WholesaleController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public async Task<IActionResult> Apply()
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.WholesaleAccounts.FirstOrDefaultAsync(w => w.UserId == userId);
        if (existing != null)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(new WholesaleApplyViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(WholesaleApplyViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.WholesaleAccounts.FirstOrDefaultAsync(w => w.UserId == userId);
        if (existing != null)
        {
            return RedirectToAction(nameof(Index));
        }

        var account = new WholesaleAccount
        {
            UserId = userId,
            CompanyName = model.CompanyName,
            TradeLicenseNumber = model.TradeLicenseNumber
        };

        _context.WholesaleAccounts.Add(account);
        await _context.SaveChangesAsync();

        var user = (await _userManager.GetUserAsync(User))!;
        await _userManager.AddToRoleAsync(user, "Wholesale");

        // Refresh the auth cookie so [Authorize(Roles="Wholesale")] recognizes the new role right away.
        await _signInManager.RefreshSignInAsync(user);

        TempData["CartMessage"] = "Welcome to the World Link Master Trade Program! Wholesale pricing is now active on your account.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Wholesale")]
    public async Task<IActionResult> Index()
    {
        var products = await _context.Products
            .Include(p => p.Category)
            .Where(p => p.WholesalePrice != null)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return View(products);
    }
}
