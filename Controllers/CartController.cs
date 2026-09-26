using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Extensions;
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

    // Deliberately uncached and separate from any full-page render — the header cart badge
    // reads session state (Areas/Admin aside, carts work for anonymous guests too), and
    // Home/Products/Index/Details are now output-cached for anonymous requests. A cached
    // page's server-rendered badge would freeze at whatever count happened to be true for the
    // first visitor who populated that cache entry, so the layout renders the badge normally
    // (still correct on every non-cached response) and re-fetches it client-side after load —
    // see the script in _Layout.cshtml — to correct a stale cached value, if the page came
    // from the cache at all.
    [HttpGet]
    public IActionResult Count()
    {
        return Json(new { count = _cartService.GetItemCount() });
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
                StockQuantity = variant?.StockQuantity ?? product.StockQuantity,
                Slug = product.Slug
            };
        }

        if (items.Count > 0)
        {
            var lastItem = items[^1];
            if (products.TryGetValue(lastItem.ProductId, out var lastProduct))
            {
                vm.LastAddedProductSlug = lastProduct.Slug;
                vm.LastAddedColor = lastItem.Color;
            }
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
            .Include(p => p.ProductColors).ThenInclude(pc => pc.Color)
            .Include(p => p.ProductColors).ThenInclude(pc => pc.Media)
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

        var cartImageUrl = ResolveColorImageUrl(product, color, variant);
        _cartService.AddToCart(product, quantity < 1 ? 1 : quantity, color, size, unitPrice, cartImageUrl);
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

    // Resolves the image for the specific color just added — the product's own gallery photo
    // for that ProductColor (imported products), else its swatch image, else the legacy
    // per-variant ImageUrl, else the product's default image. Mirrors the same fallback chain
    // Details.cshtml already uses for its own color gallery, just without the shared/color-scope
    // merging that page's full gallery needs (a cart thumbnail only ever shows one photo).
    private static string? ResolveColorImageUrl(Product product, string? color, ProductVariant? variant)
    {
        if (string.IsNullOrEmpty(color))
        {
            return product.ImageUrl;
        }

        var productColor = product.ProductColors.FirstOrDefault(pc => string.Equals(pc.Color?.Name, color, StringComparison.OrdinalIgnoreCase));
        var ownMedia = productColor?.Media
            .Where(m => m.ShowInGallery && ImagePlaceholder.IsRealImageUrl(m.MediaUrl))
            .OrderByDescending(m => m.IsColorMain)
            .ThenBy(m => m.DisplayOrder)
            .FirstOrDefault();
        if (ownMedia != null)
        {
            return ownMedia.MediaUrl;
        }

        if (ImagePlaceholder.IsRealImageUrl(productColor?.SwatchImageUrl))
        {
            return productColor!.SwatchImageUrl;
        }

        if (ImagePlaceholder.IsRealImageUrl(variant?.ImageUrl))
        {
            return variant!.ImageUrl;
        }

        return product.ImageUrl;
    }

    // Fetched via AJAX right after a successful Add, so the drawer never needs a full page
    // reload — reuses the exact same BuildCartViewModelAsync() as the cart page itself, so
    // stock/SKU/price enrichment and coupon state are always consistent between the two.
    [HttpGet]
    public async Task<IActionResult> Drawer()
    {
        var vm = await BuildCartViewModelAsync();
        return PartialView("_CartDrawer", vm);
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
    public async Task<IActionResult> Remove(int productId, string? color = null, string? size = null)
    {
        _cartService.RemoveFromCart(productId, color, size);

        // Existing plain-form callers (the cart page's own remove button) are unaffected — this
        // branch is only reached by the cart drawer, which sends the AJAX header, exactly like
        // UpdateQuantityAjax's dual-mode pattern above.
        if (IsAjax())
        {
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
                itemCount = _cartService.GetItemCount(),
                isEmpty = vm.Items.Count == 0
            });
        }

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
