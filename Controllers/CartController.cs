using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class CartController : Controller
{
    private const string AppliedCouponSessionKey = "AppliedCouponCode";

    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly IPromoService _promoService;
    private readonly ICouponService _couponService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CartController(
        ICartService cartService,
        ApplicationDbContext context,
        IPromoService promoService,
        ICouponService couponService,
        UserManager<ApplicationUser> userManager,
        IStringLocalizer<SharedResource> localizer)
    {
        _cartService = cartService;
        _context = context;
        _promoService = promoService;
        _couponService = couponService;
        _userManager = userManager;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var vm = await BuildCartViewModelAsync();
        return View(vm);
    }

    // Shared by Index() and UpdateQuantityAjax() so the page and the AJAX stepper always agree
    // on stock/SKU enrichment and the currently-applied coupon.
    private async Task<CartViewModel> BuildCartViewModelAsync()
    {
        var items = _cartService.GetCart();
        var vm = new CartViewModel { Items = items };

        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        foreach (var item in items)
        {
            var lineKey = $"{item.ProductId}|{item.Color}|{item.Size}";
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                continue;
            }

            ProductVariant? variant = null;
            if (product.Variants.Count > 0)
            {
                variant = product.Variants.FirstOrDefault(v =>
                    string.Equals(v.Color?.Name, item.Color, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(v.Size?.Label, item.Size, StringComparison.OrdinalIgnoreCase));
            }

            vm.StockInfoByLineKey[lineKey] = new CartLineStockInfo
            {
                Sku = !string.IsNullOrEmpty(variant?.Sku) ? variant.Sku : product.Sku,
                StockQuantity = variant?.StockQuantity ?? product.StockQuantity
            };
        }

        await ApplyStoredCouponAsync(vm);

        return vm;
    }

    // Coupons are always validated against a user id, so only attempt to reapply a stored
    // coupon for signed-in visitors — anonymous cart browsing otherwise works fine without one.
    private async Task ApplyStoredCouponAsync(CartViewModel vm)
    {
        var storedCode = HttpContext.Session.GetString(AppliedCouponSessionKey);
        if (string.IsNullOrEmpty(storedCode) || User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = _userManager.GetUserId(User)!;
        var coupon = await _couponService.ValidateAsync(storedCode, userId);
        if (coupon == null)
        {
            HttpContext.Session.Remove(AppliedCouponSessionKey);
            return;
        }

        vm.AppliedCouponCode = coupon.Code;
        vm.CouponDiscountPercent = coupon.DiscountPercent;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? color = null, string? size = null, bool redirectToCheckout = false)
    {
        var product = await _context.Products
            .Include(p => p.Variants).ThenInclude(v => v.Color)
            .Include(p => p.Variants).ThenInclude(v => v.Size)
            .FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null)
        {
            return IsAjax() ? NotFound(new { message = _localizer["That product couldn't be found."].Value }) : NotFound();
        }

        // Products with real variants (colors/sizes) track stock per combo — the plain
        // Product.StockQuantity only governs products with nothing to select.
        ProductVariant? variant = null;
        if (product.Variants.Count > 0)
        {
            variant = product.Variants.FirstOrDefault(v =>
                string.Equals(v.Color?.Name, color, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v.Size?.Label, size, StringComparison.OrdinalIgnoreCase));

            if (variant == null)
            {
                string invalidOptionMessage = _localizer["Please choose a valid color/size option."];
                if (IsAjax())
                {
                    return BadRequest(new { message = invalidOptionMessage });
                }
                TempData["CartMessage"] = invalidOptionMessage;
                return RedirectToAction("Details", "Products", new { slug = product.Slug });
            }
        }

        var availableStock = variant?.StockQuantity ?? product.StockQuantity;
        if (availableStock <= 0)
        {
            string outOfStockMessage = _localizer["Sorry, that item just sold out."];
            if (IsAjax())
            {
                return BadRequest(new { message = outOfStockMessage });
            }
            TempData["CartMessage"] = outOfStockMessage;
            return RedirectToAction("Details", "Products", new { slug = product.Slug });
        }

        // A variant with its own Price/WholesalePrice overrides the product's base price;
        // null on the variant means "use the product's price" (see ProductVariant.Price).
        var basePrice = variant?.Price ?? product.Price;
        var baseWholesalePrice = variant?.WholesalePrice ?? product.WholesalePrice;

        var wholesaleEligible = baseWholesalePrice.HasValue && User.IsInRole("Wholesale");
        var unitPrice = wholesaleEligible ? baseWholesalePrice!.Value : basePrice;

        string? saleMessage = null;
        if (!wholesaleEligible)
        {
            var activeEvent = await _promoService.GetTopActiveEventAsync();
            if (activeEvent != null)
            {
                unitPrice = _promoService.ApplyDiscount(basePrice, activeEvent.DiscountPercent);
                saleMessage = _localizer["{0} added to your cart at the {1} price ({2}% off)!", product.Name, activeEvent.Name, activeEvent.DiscountPercent.ToString("0.##")];
            }
        }

        _cartService.AddToCart(product, quantity < 1 ? 1 : quantity, color, size, unitPrice);
        var message = saleMessage ?? (wholesaleEligible
            ? _localizer["{0} added to your cart at your wholesale price.", product.Name].Value
            : _localizer["{0} added to your cart.", product.Name].Value);

        if (IsAjax())
        {
            return Json(new { success = true, message, cartCount = _cartService.GetItemCount() });
        }

        TempData["CartMessage"] = message;

        // "Buy Now" — same add-to-cart path, but goes straight to checkout instead of back
        // to the product page.
        if (redirectToCheckout)
        {
            return RedirectToAction("Index", "Checkout");
        }

        return RedirectToAction("Details", "Products", new { slug = product.Slug });
    }

    private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateQuantity(int productId, int quantity, string? color = null, string? size = null)
    {
        _cartService.UpdateQuantity(productId, color, size, quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateQuantityAjax(int productId, string? color, string? size, int quantity)
    {
        quantity = Math.Max(1, quantity);
        _cartService.UpdateQuantity(productId, color, size, quantity);
        var vm = await BuildCartViewModelAsync();
        return Json(new
        {
            success = true,
            subtotal = vm.Subtotal,
            shippingCost = vm.ShippingCost,
            total = vm.Total,
            amountAwayFromFreeShipping = vm.AmountAwayFromFreeShipping,
            qualifiesForFreeShipping = vm.QualifiesForFreeShipping,
            couponDiscountAmount = vm.CouponDiscountAmount,
            itemCount = _cartService.GetItemCount()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Remove(int productId, string? color = null, string? size = null)
    {
        _cartService.RemoveFromCart(productId, color, size);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ClearCart()
    {
        _cartService.ClearCart();
        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string couponCode)
    {
        var userId = _userManager.GetUserId(User)!;
        var coupon = await _couponService.ValidateAsync(couponCode ?? string.Empty, userId);
        if (coupon == null)
        {
            TempData["CartMessage"] = _localizer["That coupon code isn't valid, has already been used, or has expired."].Value;
        }
        else
        {
            HttpContext.Session.SetString(AppliedCouponSessionKey, coupon.Code);
            TempData["CartMessage"] = _localizer["Coupon {0} applied — {1}% off!", coupon.Code, coupon.DiscountPercent.ToString("0.##")].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveCoupon()
    {
        HttpContext.Session.Remove(AppliedCouponSessionKey);
        TempData["CartMessage"] = _localizer["Coupon removed."].Value;
        return RedirectToAction(nameof(Index));
    }
}
