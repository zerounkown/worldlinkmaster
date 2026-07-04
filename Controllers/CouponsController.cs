using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class CouponsController : Controller
{
    private readonly ICouponService _couponService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CouponsController(ICouponService couponService, UserManager<ApplicationUser> userManager)
    {
        _couponService = couponService;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User)!;
        var coupons = await _couponService.GetUserCouponsAsync(userId);
        return View(coupons);
    }
}
