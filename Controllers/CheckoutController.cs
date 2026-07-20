using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Stripe.Checkout;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private const string PendingShippingSessionKey = "PendingShipping";
    private const string AppliedCouponSessionKey = "AppliedCouponCode";
    private const string OtpCodeSessionKey = "CheckoutOtpCode";
    private const string OtpExpirySessionKey = "CheckoutOtpExpiry";
    private const string OtpAttemptsSessionKey = "CheckoutOtpAttempts";
    private const string OtpEmailSessionKey = "CheckoutOtpEmail";
    private const int OtpValidityMinutes = 10;
    private const int OtpMaxAttempts = 5;

    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly ICouponService _couponService;
    private readonly IEmailService _emailService;
    private readonly IOrderFulfillmentService _fulfillment;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<CheckoutController> _logger;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public CheckoutController(
        ICartService cartService,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ICouponService couponService,
        IEmailService emailService,
        IOrderFulfillmentService fulfillment,
        IHostEnvironment environment,
        ILogger<CheckoutController> logger,
        IStringLocalizer<SharedResource> localizer)
    {
        _cartService = cartService;
        _context = context;
        _userManager = userManager;
        _configuration = configuration;
        _couponService = couponService;
        _emailService = emailService;
        _fulfillment = fulfillment;
        _environment = environment;
        _logger = logger;
        _localizer = localizer;
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

        await ApplyStoredCouponAsync(vm);

        ViewBag.StripeConfigured = !string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyCoupon(string couponCode)
    {
        var userId = _userManager.GetUserId(User)!;
        var coupon = await _couponService.ValidateAsync(couponCode ?? string.Empty, userId);
        if (coupon == null)
        {
            TempData["CheckoutMessage"] = _localizer["That coupon code isn't valid, has already been used, or has expired."].Value;
        }
        else
        {
            HttpContext.Session.SetString(AppliedCouponSessionKey, coupon.Code);
            TempData["CheckoutMessage"] = _localizer["Coupon {0} applied — {1}% off!", coupon.Code, coupon.DiscountPercent.ToString("0.##")].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveCoupon()
    {
        HttpContext.Session.Remove(AppliedCouponSessionKey);
        TempData["CheckoutMessage"] = _localizer["Coupon removed."].Value;
        return RedirectToAction(nameof(Index));
    }

    private async Task ApplyStoredCouponAsync(CheckoutViewModel vm)
    {
        var storedCode = HttpContext.Session.GetString(AppliedCouponSessionKey);
        if (string.IsNullOrEmpty(storedCode))
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
    public async Task<IActionResult> PlaceOrder(CheckoutViewModel model)
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        model.Items = cart;
        await ApplyStoredCouponAsync(model);

        if (!ModelState.IsValid)
        {
            return View("Index", model);
        }

        if (string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]))
        {
            ModelState.AddModelError(string.Empty, _localizer["Stripe isn't configured yet. Set the Stripe__SecretKey environment variable to enable checkout."]);
            return View("Index", model);
        }

        // Stash the shipping form so it survives the redirect through OTP verification and out to Stripe.
        HttpContext.Session.SetString(PendingShippingSessionKey, JsonSerializer.Serialize(model));

        var user = await _userManager.GetUserAsync(User);
        var email = user?.Email ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(string.Empty, _localizer["We couldn't find an email address on your account to send a verification code to."]);
            return View("Index", model);
        }

        _logger.LogInformation("Checkout started by user {UserId}; sending verification code.", _userManager.GetUserId(User));
        await SendOtpAsync(email);

        return RedirectToAction(nameof(VerifyOtp));
    }

    public IActionResult VerifyOtp()
    {
        if (string.IsNullOrEmpty(HttpContext.Session.GetString(PendingShippingSessionKey)) ||
            string.IsNullOrEmpty(HttpContext.Session.GetString(OtpCodeSessionKey)))
        {
            return RedirectToAction(nameof(Index));
        }

        SetOtpViewBag();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOtp(string code)
    {
        var shippingJson = HttpContext.Session.GetString(PendingShippingSessionKey);
        var storedCode = HttpContext.Session.GetString(OtpCodeSessionKey);
        var storedExpiry = HttpContext.Session.GetString(OtpExpirySessionKey);

        if (string.IsNullOrEmpty(shippingJson) || string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(storedExpiry))
        {
            TempData["CheckoutMessage"] = _localizer["Your checkout session expired. Please start again."].Value;
            return RedirectToAction(nameof(Index));
        }

        var model = JsonSerializer.Deserialize<CheckoutViewModel>(shippingJson)!;
        var attempts = int.TryParse(HttpContext.Session.GetString(OtpAttemptsSessionKey), out var a) ? a : 0;

        if (attempts >= OtpMaxAttempts)
        {
            ModelState.AddModelError(string.Empty, _localizer["Too many incorrect attempts. Request a new code."]);
            SetOtpViewBag();
            return View();
        }

        if (DateTime.UtcNow > DateTime.Parse(storedExpiry).ToUniversalTime())
        {
            ModelState.AddModelError(string.Empty, _localizer["That code has expired. Request a new one."]);
            SetOtpViewBag();
            return View();
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim() != storedCode)
        {
            HttpContext.Session.SetString(OtpAttemptsSessionKey, (attempts + 1).ToString());
            ModelState.AddModelError(string.Empty, _localizer["That code isn't correct. Please try again."]);
            SetOtpViewBag();
            return View();
        }

        HttpContext.Session.Remove(OtpCodeSessionKey);
        HttpContext.Session.Remove(OtpExpirySessionKey);
        HttpContext.Session.Remove(OtpAttemptsSessionKey);
        HttpContext.Session.Remove(OtpEmailSessionKey);

        return await CreateStripeCheckoutSessionAsync(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendOtp()
    {
        var email = HttpContext.Session.GetString(OtpEmailSessionKey);
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(HttpContext.Session.GetString(PendingShippingSessionKey)))
        {
            return RedirectToAction(nameof(Index));
        }

        await SendOtpAsync(email);
        TempData["CheckoutMessage"] = _localizer["We sent you a new code."].Value;
        return RedirectToAction(nameof(VerifyOtp));
    }

    private async Task SendOtpAsync(string email)
    {
        var code = Random.Shared.Next(100000, 999999).ToString();
        HttpContext.Session.SetString(OtpCodeSessionKey, code);
        HttpContext.Session.SetString(OtpExpirySessionKey, DateTime.UtcNow.AddMinutes(OtpValidityMinutes).ToString("o"));
        HttpContext.Session.SetString(OtpAttemptsSessionKey, "0");
        HttpContext.Session.SetString(OtpEmailSessionKey, email);

        await _emailService.SendOtpAsync(email, code);
    }

    private void SetOtpViewBag()
    {
        ViewBag.OtpEmail = HttpContext.Session.GetString(OtpEmailSessionKey);
        ViewBag.EmailConfigured = _emailService.IsConfigured;
        // SECURITY: the OTP is only ever revealed on-screen in Development. In every other
        // environment it is delivered exclusively by email.
        ViewBag.DevOtpCode = _environment.IsDevelopment() ? HttpContext.Session.GetString(OtpCodeSessionKey) : null;
    }

    private async Task<IActionResult> CreateStripeCheckoutSessionAsync(CheckoutViewModel model)
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        var userId = _userManager.GetUserId(User)!;
        var discountedCart = ApplyCouponToCart(cart, model.CouponDiscountPercent);

        // Persist the order as unpaid BEFORE redirecting to Stripe. Payment is then confirmed
        // server-side by the Stripe webhook, independent of the browser returning to /Success.
        var order = await _fulfillment.CreatePendingOrderAsync(userId, model, discountedCart);

        var lineItems = discountedCart.Select(item => new SessionLineItemOptions
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
            CustomerEmail = User.Identity?.Name,
            ClientReferenceId = order.Id.ToString(),
            Metadata = new Dictionary<string, string>
            {
                ["OrderId"] = order.Id.ToString(),
                ["UserId"] = userId
            }
        };

        try
        {
            var service = new SessionService();
            Session session = await service.CreateAsync(options);

            order.StripeSessionId = session.Id;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Stripe checkout session {SessionId} created for order {OrderId}.", session.Id, order.Id);
            return Redirect(session.Url);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe failed to start checkout for order {OrderId}.", order.Id);
            TempData["CheckoutMessage"] = _localizer["Stripe couldn't start checkout: {0}", ex.StripeError?.Message ?? ex.Message].Value;
            return RedirectToAction(nameof(Index));
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
            TempData["CartMessage"] = _localizer["We couldn't verify that payment with Stripe. Please try checking out again."].Value;
            return RedirectToAction("Index", "Cart");
        }

        if (session.PaymentStatus != "paid")
        {
            TempData["CartMessage"] = _localizer["Your payment could not be confirmed. Please try again."].Value;
            return RedirectToAction(nameof(Index));
        }

        var userId = _userManager.GetUserId(User)!;
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.StripeSessionId == session_id && o.UserId == userId);

        // Fallback to the session metadata if the session id wasn't persisted for some reason.
        if (order == null && session.Metadata != null &&
            session.Metadata.TryGetValue("OrderId", out var orderIdRaw) && int.TryParse(orderIdRaw, out var metadataOrderId))
        {
            order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == metadataOrderId && o.UserId == userId);
        }

        if (order == null)
        {
            // Nothing to show for this user/session — send them back to the catalog.
            return RedirectToAction("Index", "Products");
        }

        // Idempotent: if the webhook already fulfilled this order, NewlyPaid is false and we
        // simply show the confirmation. Otherwise this call performs fulfillment exactly once.
        var result = await _fulfillment.FulfillPaidOrderAsync(order.Id, session.PaymentIntentId);
        if (result.NewlyPaid && !string.IsNullOrEmpty(result.LoyaltyCouponCode))
        {
            TempData["NewLoyaltyCouponCode"] = result.LoyaltyCouponCode;
            TempData["NewLoyaltyDiscountPercent"] = result.LoyaltyDiscountPercent.ToString("0.##");
        }

        _cartService.ClearCart();
        HttpContext.Session.Remove(PendingShippingSessionKey);
        HttpContext.Session.Remove(AppliedCouponSessionKey);

        return RedirectToAction(nameof(Confirmation), new { id = order.Id });
    }

    public IActionResult Cancel()
    {
        TempData["CartMessage"] = _localizer["Checkout was cancelled. Your cart is still saved."].Value;
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

    private static List<CartItem> ApplyCouponToCart(List<CartItem> cart, decimal discountPercent)
    {
        if (discountPercent <= 0)
        {
            return cart;
        }

        var multiplier = 1 - discountPercent / 100m;
        return cart.Select(i => new CartItem
        {
            ProductId = i.ProductId,
            Name = i.Name,
            UnitPrice = Math.Round(i.UnitPrice * multiplier, 2),
            ImageUrl = i.ImageUrl,
            Quantity = i.Quantity,
            Color = i.Color,
            Size = i.Size,
            MerchantId = i.MerchantId
        }).ToList();
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
