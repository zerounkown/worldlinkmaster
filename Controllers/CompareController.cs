using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class CompareController : Controller
{
    // Beyond this many columns the comparison table stops being readable — matches common
    // storefront conventions (most sites cap compare at 4).
    private const int MaxCompareItems = 4;

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CompareController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _userManager = userManager;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var products = await _context.CompareItems
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CreatedAt)
            .Include(c => c.Product!).ThenInclude(p => p.Brand)
            .Include(c => c.Product!).ThenInclude(p => p.Category)
            .Include(c => c.Product!).ThenInclude(p => p.AttributeValues.Where(a => a.Active && a.AttributeDefinition!.Code != "FEATURE"))
                .ThenInclude(a => a.AttributeDefinition)
            .Select(c => c.Product!)
            .ToListAsync();

        return View(products);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int productId, string? returnUrl)
    {
        var userId = _userManager.GetUserId(User)!;
        var productExists = await _context.Products.AnyAsync(p => p.Id == productId);
        if (!productExists)
        {
            return IsAjax() ? NotFound(new { message = _localizer["That product couldn't be found."].Value }) : NotFound();
        }

        var existing = await _context.CompareItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);
        bool isInCompare;
        string message;
        if (existing != null)
        {
            _context.CompareItems.Remove(existing);
            isInCompare = false;
            message = _localizer["Removed from comparison."];
        }
        else
        {
            var currentCount = await _context.CompareItems.CountAsync(c => c.UserId == userId);
            if (currentCount >= MaxCompareItems)
            {
                var limitMessage = _localizer["You can compare up to {0} products at a time — remove one first.", MaxCompareItems].Value;
                return IsAjax()
                    ? BadRequest(new { message = limitMessage })
                    : RedirectToAction(nameof(Index));
            }

            _context.CompareItems.Add(new CompareItem { UserId = userId, ProductId = productId });
            isInCompare = true;
            message = _localizer["Added to comparison."];
        }

        await _context.SaveChangesAsync();
        var compareCount = await _context.CompareItems.CountAsync(c => c.UserId == userId);

        if (IsAjax())
        {
            return Json(new { success = true, isInCompare, compareCount, message });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId)
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.CompareItems.FirstOrDefaultAsync(c => c.UserId == userId && c.ProductId == productId);
        if (existing != null)
        {
            _context.CompareItems.Remove(existing);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
}
