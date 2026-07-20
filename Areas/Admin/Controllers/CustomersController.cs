using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class CustomersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CustomersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _userManager = userManager;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _userManager.Users.OrderBy(u => u.Email).ToListAsync();
        var orderCounts = await _context.Orders
            .GroupBy(o => o.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count);

        var items = new List<CustomerListItemViewModel>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            items.Add(new CustomerListItemViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Roles = roles,
                LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
                EmailConfirmed = user.EmailConfirmed,
                OrderCount = orderCounts.GetValueOrDefault(user.Id)
            });
        }

        return View(items);
    }

    public async Task<IActionResult> Details(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var vm = new CustomerDetailsViewModel
        {
            User = user,
            Roles = await _userManager.GetRolesAsync(user),
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            Orders = await _context.Orders
                .Where(o => o.UserId == id)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync(),
            MerchantProfile = await _context.Merchants.FirstOrDefaultAsync(m => m.UserId == id),
            WholesaleAccount = await _context.WholesaleAccounts.FirstOrDefaultAsync(w => w.UserId == id),
            CouponCount = await _context.Coupons.CountAsync(c => c.UserId == id)
        };

        return View(vm);
    }

    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var roles = await _userManager.GetRolesAsync(user);
        var vm = new CustomerEditViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
            EmailConfirmed = user.EmailConfirmed,
            LockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            IsAdmin = roles.Contains("Admin"),
            IsMerchant = roles.Contains("Merchant"),
            IsWholesale = roles.Contains("Wholesale"),
            IsCustomer = roles.Contains("Customer"),
            IsSupport = roles.Contains("Support")
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, CustomerEditViewModel model)
    {
        if (id != model.Id)
        {
            return NotFound();
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // An admin can't lock themselves out or strip their own Admin role by mistake.
        var isSelf = _userManager.GetUserId(User) == id;
        if (isSelf)
        {
            model.LockedOut = false;
            model.IsAdmin = true;
        }

        user.FirstName = model.FirstName;
        user.LastName = model.LastName;
        user.PhoneNumber = model.PhoneNumber;
        user.EmailConfirmed = model.EmailConfirmed;
        user.LockoutEnabled = true;
        user.LockoutEnd = model.LockedOut ? DateTimeOffset.MaxValue : null;

        if (!string.Equals(user.Email, model.Email, StringComparison.OrdinalIgnoreCase))
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, model.Email);
            if (!setEmailResult.Succeeded)
            {
                foreach (var error in setEmailResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }
            await _userManager.SetUserNameAsync(user, model.Email);
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var error in updateResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await SyncRoleAsync(user, "Admin", model.IsAdmin);
        await SyncRoleAsync(user, "Merchant", model.IsMerchant);
        await SyncRoleAsync(user, "Wholesale", model.IsWholesale);
        await SyncRoleAsync(user, "Customer", model.IsCustomer);
        await SyncRoleAsync(user, "Support", model.IsSupport);

        TempData["AdminMessage"] = _localizer["Customer '{0}' updated.", user.Email].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        if (_userManager.GetUserId(User) == id)
        {
            TempData["AdminMessage"] = _localizer["You cannot delete your own account."].Value;
            return RedirectToAction(nameof(Index));
        }

        ViewBag.OrderCount = await _context.Orders.CountAsync(o => o.UserId == id);
        ViewBag.HasMerchantProfile = await _context.Merchants.AnyAsync(m => m.UserId == id);
        ViewBag.HasWholesaleAccount = await _context.WholesaleAccounts.AnyAsync(w => w.UserId == id);
        ViewBag.CouponCount = await _context.Coupons.CountAsync(c => c.UserId == id); // informational only — deleted along with the account, not a blocker

        return View(user);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        if (_userManager.GetUserId(User) == id)
        {
            TempData["AdminMessage"] = _localizer["You cannot delete your own account."].Value;
            return RedirectToAction(nameof(Index));
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var blockers = new List<string>();
        if (await _context.Orders.AnyAsync(o => o.UserId == id)) blockers.Add(_localizer["Order History"].Value);
        if (await _context.Merchants.AnyAsync(m => m.UserId == id)) blockers.Add(_localizer["a merchant profile"].Value);
        if (await _context.WholesaleAccounts.AnyAsync(w => w.UserId == id)) blockers.Add(_localizer["a wholesale account"].Value);

        if (blockers.Count > 0)
        {
            TempData["AdminMessage"] = _localizer["Can't delete '{0}' — this customer still has {1}. Lock the account instead (Edit > Lock account) if you need to disable it.", user.Email, string.Join(", ", blockers)].Value;
            return RedirectToAction(nameof(Index));
        }

        // Personal coupons (welcome/loyalty) have no significance once the owner is gone — any
        // coupon that was actually redeemed lives on an Order, which would have blocked us above.
        var personalCoupons = await _context.Coupons.Where(c => c.UserId == id).ToListAsync();
        _context.Coupons.RemoveRange(personalCoupons);
        await _context.SaveChangesAsync();

        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["AdminMessage"] = _localizer["Could not delete '{0}': {1}", user.Email, string.Join(" ", result.Errors.Select(e => e.Description))].Value;
            return RedirectToAction(nameof(Index));
        }

        TempData["AdminMessage"] = _localizer["Customer '{0}' deleted.", user.Email].Value;
        return RedirectToAction(nameof(Index));
    }

    private async Task SyncRoleAsync(ApplicationUser user, string role, bool shouldHaveRole)
    {
        var hasRole = await _userManager.IsInRoleAsync(user, role);
        if (shouldHaveRole && !hasRole)
        {
            await _userManager.AddToRoleAsync(user, role);
        }
        else if (!shouldHaveRole && hasRole)
        {
            await _userManager.RemoveFromRoleAsync(user, role);
        }
    }
}
