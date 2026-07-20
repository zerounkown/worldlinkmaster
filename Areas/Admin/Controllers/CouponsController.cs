using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class CouponsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CouponsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var coupons = await _context.Coupons
            .Where(c => c.Source == CouponSource.AdminPromo)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(coupons);
    }

    public IActionResult Create()
    {
        return View(new Coupon
        {
            Source = CouponSource.AdminPromo,
            DiscountPercent = 10,
            StartsAt = DateTime.UtcNow.Date,
            ExpiresAt = DateTime.UtcNow.Date.AddDays(30),
            IsActive = true
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Coupon coupon)
    {
        // Public promo coupons are never tied to a user.
        coupon.UserId = null;
        coupon.Source = CouponSource.AdminPromo;
        coupon.Code = (coupon.Code ?? string.Empty).Trim().ToUpperInvariant();

        await ValidateCodeAsync(coupon, currentId: null);
        ValidateDates(coupon);

        if (!ModelState.IsValid)
        {
            return View(coupon);
        }

        coupon.RedemptionCount = 0;
        coupon.IsUsed = false;
        coupon.CreatedAt = DateTime.UtcNow;

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Coupon '{0}' created.", coupon.Code].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.Source == CouponSource.AdminPromo);
        if (coupon == null)
        {
            return NotFound();
        }

        return View(coupon);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Coupon coupon)
    {
        if (id != coupon.Id)
        {
            return NotFound();
        }

        var existing = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.Source == CouponSource.AdminPromo);
        if (existing == null)
        {
            return NotFound();
        }

        coupon.Code = (coupon.Code ?? string.Empty).Trim().ToUpperInvariant();
        await ValidateCodeAsync(coupon, currentId: id);
        ValidateDates(coupon);

        if (!ModelState.IsValid)
        {
            return View(coupon);
        }

        // Only the editable fields are copied; RedemptionCount/CreatedAt are preserved from the DB.
        existing.Code = coupon.Code;
        existing.Description = coupon.Description;
        existing.DiscountPercent = coupon.DiscountPercent;
        existing.StartsAt = coupon.StartsAt;
        existing.ExpiresAt = coupon.ExpiresAt;
        existing.MaxRedemptions = coupon.MaxRedemptions;
        existing.IsActive = coupon.IsActive;

        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Coupon '{0}' updated.", existing.Code].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.Source == CouponSource.AdminPromo);
        if (coupon == null)
        {
            return NotFound();
        }

        return View(coupon);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Id == id && c.Source == CouponSource.AdminPromo);
        if (coupon != null)
        {
            _context.Coupons.Remove(coupon);
            await _context.SaveChangesAsync();
            TempData["AdminMessage"] = _localizer["Coupon '{0}' deleted.", coupon.Code].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateCodeAsync(Coupon coupon, int? currentId)
    {
        if (string.IsNullOrWhiteSpace(coupon.Code))
        {
            ModelState.AddModelError(nameof(Coupon.Code), _localizer["A coupon code is required."]);
            return;
        }

        var codeTaken = await _context.Coupons
            .AnyAsync(c => c.Code == coupon.Code && (currentId == null || c.Id != currentId));
        if (codeTaken)
        {
            ModelState.AddModelError(nameof(Coupon.Code), _localizer["That code is already in use. Choose a different one."]);
        }
    }

    private void ValidateDates(Coupon coupon)
    {
        if (coupon.StartsAt.HasValue && coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < coupon.StartsAt.Value)
        {
            ModelState.AddModelError(nameof(Coupon.ExpiresAt), _localizer["The expiry date must be on or after the start date."]);
        }

        if (coupon.MaxRedemptions.HasValue && coupon.MaxRedemptions.Value < 1)
        {
            ModelState.AddModelError(nameof(Coupon.MaxRedemptions), _localizer["Max redemptions must be at least 1 (leave blank for unlimited)."]);
        }
    }
}
