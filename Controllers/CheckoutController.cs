using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private const string PendingShippingSessionKey = "PendingShipping";
    private const decimal PlatformCommissionRate = 0.10m;

    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IStripeConnectService _stripeConnect;

    public CheckoutController(ICartService cartService, ApplicationDbContext context, UserManager<ApplicationUser> userManager, IConfiguration configuration, IStripeConnectService stripeConnect)
    {
        _cartService = cartService;
        _context = context;
        _userManager = userManager;
        _configuration = configuration;
        _stripeConnect = stripeConnect;
    }

    public async Task<IActionResult> Index()
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        var user = await _userManager.GetUserAsync(User);
        var vm = new CheckoutViewModel
        {
            Items = cart,
            ShippingName = user != null ? $"{user.FirstName} {user.LastName}".Trim() : string.Empty
        };

        ViewBag.StripeConfigured = !string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult PlaceOrder(CheckoutViewModel model)
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        model.Items = cart;

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        if (string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]))
        {
            ModelState.AddModelError(string.Empty, "Stripe isn't configured yet. Add your Stripe test API keys to appsettings.json to enable checkout.");
            return View("Index", model);
        }

        // Stash the shipping form so it survives the redirect out to Stripe and back.
        HttpContext.Session.SetString(PendingShippingSessionKey, JsonSerializer.Serialize(model));

        var lineItems = cart.Select(item => new SessionLineItemOptions
        {
            Quantity = item.Quantity,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "aed",
                UnitAmount = (long)Math.Round(item.UnitPrice * 100),
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = item.Name,
                    Description = BuildVariantDescription(item),
                    Images = string.IsNullOrEmpty(item.ImageUrl) ? null : new List<string> { item.ImageUrl }
                }
            }
        }).ToList();

        if (model.ShippingCost > 0)
        {
            lineItems.Add(new SessionLineItemOptions
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "aed",
                    UnitAmount = (long)Math.Round(model.ShippingCost * 100),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = "Shipping"
                    }
                }
            });
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var options = new SessionCreateOptions
        {
            Mode = "payment",
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = lineItems,
            SuccessUrl = $"{baseUrl}/Checkout/Success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{baseUrl}/Checkout/Cancel",
            CustomerEmail = User.Identity?.Name
        };

        try
        {
            var service = new SessionService();
            Session session = service.Create(options);
            return Redirect(session.Url);
        }
        catch (Stripe.StripeException ex)
        {
            ModelState.AddModelError(string.Empty, $"Stripe couldn't start checkout: {ex.StripeError?.Message ?? ex.Message}");
            return View("Index", model);
        }
    }

    public async Task<IActionResult> Success(string session_id)
    {
        if (string.IsNullOrEmpty(session_id))
        {
            return RedirectToAction("Index", "Cart");
        }

        Session session;
        try
        {
            var sessionService = new SessionService();
            session = await sessionService.GetAsync(session_id);
        }
        catch (Stripe.StripeException)
        {
            TempData["CartMessage"] = "We couldn't verify that payment with Stripe. Please try checking out again.";
            return RedirectToAction("Index", "Cart");
        }

        if (session.PaymentStatus != "paid")
        {
            TempData["CartMessage"] = "Your payment could not be confirmed. Please try again.";
            return RedirectToAction(nameof(Index));
        }

        var shippingJson = HttpContext.Session.GetString(PendingShippingSessionKey);
        var shipping = string.IsNullOrEmpty(shippingJson) ? null : JsonSerializer.Deserialize<CheckoutViewModel>(shippingJson);
        var cart = _cartService.GetCart();

        if (shipping == null || cart.Count == 0)
        {
            // Already processed (e.g. page refresh) - just show the most recent order instead of duplicating it.
            var userId = _userManager.GetUserId(User);
            var existing = await _context.Orders
                .Where(o => o.UserId == userId && o.StripeSessionId == session_id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return RedirectToAction(nameof(Confirmation), new { id = existing.Id });
            }

            return RedirectToAction("Index", "Products");
        }

        var order = new Order
        {
            UserId = _userManager.GetUserId(User)!,
            ShippingName = shipping.ShippingName,
            ShippingAddress = shipping.ShippingAddress,
            ShippingCity = shipping.ShippingCity,
            ShippingState = shipping.ShippingState,
            ShippingZip = shipping.ShippingZip,
            ShippingPhone = shipping.ShippingPhone,
            Subtotal = shipping.Subtotal,
            ShippingCost = shipping.ShippingCost,
            Total = shipping.Total,
            Status = OrderStatus.Pending,
            IsPaid = true,
            StripeSessionId = session.Id,
            StripePaymentIntentId = session.PaymentIntentId,
            Items = cart.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.Name,
                UnitPrice = i.UnitPrice,
                Quantity = i.Quantity,
                LineTotal = i.LineTotal,
                Color = i.Color,
                Size = i.Size,
                MerchantId = i.MerchantId
            }).ToList()
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        await CreateMerchantPayoutsAsync(order);

        _cartService.ClearCart();
        HttpContext.Session.Remove(PendingShippingSessionKey);

        return RedirectToAction(nameof(Confirmation), new { id = order.Id });
    }

    public IActionResult Cancel()
    {
        TempData["CartMessage"] = "Checkout was cancelled. Your cart is still saved.";
        return RedirectToAction("Index", "Cart");
    }

    public async Task<IActionResult> Confirmation(int id)
    {
        var userId = _userManager.GetUserId(User);
        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    private async Task CreateMerchantPayoutsAsync(Order order)
    {
        var merchantGroups = order.Items.GroupBy(i => i.MerchantId);

        foreach (var group in merchantGroups)
        {
            var merchant = await _context.Merchants.FindAsync(group.Key);
            if (merchant == null)
            {
                continue;
            }

            var grossAmount = group.Sum(i => i.LineTotal);
            var platformFee = Math.Round(grossAmount * PlatformCommissionRate, 2);
            var netAmount = grossAmount - platformFee;

            var payout = new MerchantPayout
            {
                OrderId = order.Id,
                MerchantId = merchant.Id,
                Amount = netAmount,
                PlatformFee = platformFee
            };

            if (!string.IsNullOrEmpty(merchant.StripeAccountId) && merchant.StripeOnboardingComplete)
            {
                try
                {
                    payout.StripeTransferId = await _stripeConnect.CreateTransferAsync(merchant.StripeAccountId, netAmount, order.Id);
                    payout.Status = PayoutStatus.Transferred;
                }
                catch (Stripe.StripeException ex)
                {
                    payout.Status = PayoutStatus.Failed;
                    payout.FailureReason = ex.StripeError?.Message ?? ex.Message;
                }
            }
            else
            {
                payout.Status = PayoutStatus.Pending;
                payout.FailureReason = "Merchant has not completed Stripe onboarding yet.";
            }

            _context.MerchantPayouts.Add(payout);
        }

        await _context.SaveChangesAsync();
    }

    private static string? BuildVariantDescription(CartItem item)
    {
        if (string.IsNullOrEmpty(item.Color) && string.IsNullOrEmpty(item.Size))
        {
            return null;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(item.Color)) parts.Add($"Color: {item.Color}");
        if (!string.IsNullOrEmpty(item.Size)) parts.Add($"Size: {item.Size}");
        return string.Join(" | ", parts);
    }
}
