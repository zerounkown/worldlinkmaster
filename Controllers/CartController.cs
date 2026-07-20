using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class CartController : Controller
{
    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly IPromoService _promoService;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CartController(ICartService cartService, ApplicationDbContext context, IPromoService promoService, IStringLocalizer<SharedResource> localizer)
    {
        _cartService = cartService;
        _context = context;
        _promoService = promoService;
        _localizer = localizer;
    }

    public IActionResult Index()
    {
        var vm = new CartViewModel { Items = _cartService.GetCart() };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, int quantity = 1, string? color = null, string? size = null)
    {
        var product = await _context.Products.FindAsync(productId);
        if (product == null)
        {
            return IsAjax() ? NotFound(new { message = _localizer["That product couldn't be found."].Value }) : NotFound();
        }

        if (product.StockQuantity <= 0)
        {
            string outOfStockMessage = _localizer["Sorry, that item just sold out."];
            if (IsAjax())
            {
                return BadRequest(new { message = outOfStockMessage });
            }
            TempData["CartMessage"] = outOfStockMessage;
            return RedirectToAction("Details", "Products", new { slug = product.Slug });
        }

        var wholesaleEligible = product.WholesalePrice.HasValue && User.IsInRole("Wholesale");
        var unitPrice = wholesaleEligible ? product.WholesalePrice!.Value : product.Price;

        string? saleMessage = null;
        if (!wholesaleEligible)
        {
            var activeEvent = await _promoService.GetTopActiveEventAsync();
            if (activeEvent != null)
            {
                unitPrice = _promoService.ApplyDiscount(product.Price, activeEvent.DiscountPercent);
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
    public IActionResult Remove(int productId, string? color = null, string? size = null)
    {
        _cartService.RemoveFromCart(productId, color, size);
        return RedirectToAction(nameof(Index));
    }
}
