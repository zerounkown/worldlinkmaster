using Microsoft.AspNetCore.Mvc;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class CartController : Controller
{
    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly IPromoService _promoService;

    public CartController(ICartService cartService, ApplicationDbContext context, IPromoService promoService)
    {
        _cartService = cartService;
        _context = context;
        _promoService = promoService;
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
            return NotFound();
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
                saleMessage = $"{product.Name} added to your cart at the {activeEvent.Name} price ({activeEvent.DiscountPercent:0.##}% off)!";
            }
        }

        _cartService.AddToCart(product, quantity < 1 ? 1 : quantity, color, size, unitPrice);
        TempData["CartMessage"] = saleMessage ?? (wholesaleEligible
            ? $"{product.Name} added to your cart at your wholesale price."
            : $"{product.Name} added to your cart.");

        return RedirectToAction("Details", "Products", new { slug = product.Slug });
    }

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
