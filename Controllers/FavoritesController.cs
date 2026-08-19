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
public class FavoritesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public FavoritesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _userManager = userManager;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var products = await _context.Favorites
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Include(f => f.Product!).ThenInclude(p => p.Variants).ThenInclude(v => v.Color)
            .Include(f => f.Product!).ThenInclude(p => p.Variants).ThenInclude(v => v.Size)
            .Include(f => f.Product!).ThenInclude(p => p.Variants).ThenInclude(v => v.ProductColor)
            .Include(f => f.Product!).ThenInclude(p => p.ProductColors.Where(pc => pc.Active).OrderBy(pc => pc.DisplayOrder))
            .Include(f => f.Product!).ThenInclude(p => p.Media.Where(m => m.Active).OrderBy(m => m.DisplayOrder))
            .AsSplitQuery()
            .Select(f => f.Product!)
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

        var existing = await _context.Favorites.FirstOrDefaultAsync(f => f.UserId == userId && f.ProductId == productId);
        bool isFavorite;
        string message;
        if (existing != null)
        {
            _context.Favorites.Remove(existing);
            isFavorite = false;
            message = _localizer["Removed from your favorites."];
        }
        else
        {
            _context.Favorites.Add(new Favorite { UserId = userId, ProductId = productId });
            isFavorite = true;
            message = _localizer["Added to your favorites."];
        }

        await _context.SaveChangesAsync();
        var favoriteCount = await _context.Favorites.CountAsync(f => f.UserId == userId);

        if (IsAjax())
        {
            return Json(new { success = true, isFavorite, favoriteCount, message });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }

    private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
}
