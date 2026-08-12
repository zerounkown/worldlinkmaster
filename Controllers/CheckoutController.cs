using System.Text.Json;
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

[Authorize]
public class CheckoutController : Controller
{
    private const string PendingShippingSessionKey = "PendingShipping";
    private const string AppliedCouponSessionKey = "AppliedCouponCode";
    private const string OtpCodeSessionKey = "CheckoutOtpCode";
    private const string OtpExpirySessionKey = "CheckoutOtpExpiry";
    private const string OtpAttemptsSessionKey = "CheckoutOtpAttempts";
    private const string OtpEmailSessionKey = "CheckoutOtpEmail";
    private const string OtpVerifiedSessionKey = "CheckoutOtpVerified";
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
        ViewBag.StripePublishableKey = _configuration["Stripe:PublishableKey"];

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
    public async Task<IActionResult> SendOtpAjax([FromBody] CheckoutViewModel model)
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
            return Json(new { success = false, message = _localizer["Your cart is empty."].Value });

        model.Items = cart;
        await ApplyStoredCouponAsync(model);

        if (!TryValidateModel(model))
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return Json(new { success = false, message = string.Join(" ", errors) });
        }

        if (string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]))
            return Json(new { success = false, message = _localizer["Stripe isn't configured yet."].Value });

        HttpContext.Session.SetString(PendingShippingSessionKey, JsonSerializer.Serialize(model));

        var user = await _userManager.GetUserAsync(User);
        var email = user?.Email ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
            return Json(new { success = false, message = _localizer["We couldn't find an email address on your account."].Value });

        await SendOtpAsync(email);
        // SECURITY: the OTP is only ever revealed in the response in Development. In every other
        // environment it is delivered exclusively by email.
        return Json(new { success = true, email, devOtpCode = _environment.IsDevelopment() ? HttpContext.Session.GetString(OtpCodeSessionKey) : null });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult VerifyOtpAjax([FromBody] OtpVerifyRequest request)
    {
        var shippingJson = HttpContext.Session.GetString(PendingShippingSessionKey);
        var storedCode = HttpContext.Session.GetString(OtpCodeSessionKey);
        var storedExpiry = HttpContext.Session.GetString(OtpExpirySessionKey);

        if (string.IsNullOrEmpty(shippingJson) || string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(storedExpiry))
            return Json(new { success = false, message = _localizer["Your checkout session expired. Please start again."].Value, expired = true });

        var attempts = int.TryParse(HttpContext.Session.GetString(OtpAttemptsSessionKey), out var a) ? a : 0;
        if (attempts >= OtpMaxAttempts)
            return Json(new { success = false, message = _localizer["Too many incorrect attempts. Request a new code."].Value });

        if (DateTime.UtcNow > DateTime.Parse(storedExpiry).ToUniversalTime())
            return Json(new { success = false, message = _localizer["That code has expired. Request a new one."].Value });

        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim() != storedCode)
        {
            HttpContext.Session.SetString(OtpAttemptsSessionKey, (attempts + 1).ToString());
            return Json(new { success = false, message = _localizer["That code isn't correct. Please try again."].Value });
        }

        HttpContext.Session.Remove(OtpCodeSessionKey);
        HttpContext.Session.Remove(OtpExpirySessionKey);
        HttpContext.Session.Remove(OtpAttemptsSessionKey);
        HttpContext.Session.Remove(OtpEmailSessionKey);
        HttpContext.Session.SetString(OtpVerifiedSessionKey, "true");

        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendOtpAjax()
    {
        var email = HttpContext.Session.GetString(OtpEmailSessionKey);
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(HttpContext.Session.GetString(PendingShippingSessionKey)))
            return Json(new { success = false, message = _localizer["Your checkout session expired. Please start again."].Value });

        await SendOtpAsync(email);
        return Json(new { success = true, devOtpCode = _environment.IsDevelopment() ? HttpContext.Session.GetString(OtpCodeSessionKey) : null });
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePaymentIntent()
    {
        if (HttpContext.Session.GetString(OtpVerifiedSessionKey) != "true")
            return Json(new { success = false, message = _localizer["Please verify your email first."].Value });

        var shippingJson = HttpContext.Session.GetString(PendingShippingSessionKey);
        if (string.IsNullOrEmpty(shippingJson))
            return Json(new { success = false, message = _localizer["Your checkout session expired. Please start again."].Value });

        var model = JsonSerializer.Deserialize<CheckoutViewModel>(shippingJson)!;
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
            return Json(new { success = false, message = _localizer["Your cart is empty."].Value });

        var userId = _userManager.GetUserId(User)!;
        var discountedCart = ApplyCouponToCart(cart, model.CouponDiscountPercent);

        Order order;
        try
        {
            order = await _fulfillment.CreatePendingOrderAsync(userId, model, discountedCart);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create pending order for user {UserId} during checkout.", userId);
            return Json(new { success = false, message = _localizer["We couldn't start checkout. Please try again."].Value });
        }

        var totalAmount = discountedCart.Sum(i => i.LineTotal) + model.ShippingCost;

        // Resolved explicitly via the ApplicationUser record (not just User.Identity?.Name) so
        // Stripe associates this PaymentIntent with the actual logged-in account's real email —
        // Stripe Link otherwise autofills based on whatever email it last saw on this browser,
        // which can belong to a completely different site user on a shared/reused device.
        var currentUser = await _userManager.GetUserAsync(User);
        var userEmail = currentUser?.Email ?? User.Identity?.Name;

        var piOptions = new Stripe.PaymentIntentCreateOptions
        {
            Amount = (long)Math.Round(totalAmount * 100),
            Currency = "aed",
            // Card only — explicitly excludes Link (and any other automatic method), which was
            // surfacing a different logged-in user's saved card via its own browser-level email
            // recognition. PaymentMethodTypes and AutomaticPaymentMethods are mutually exclusive
            // in the Stripe API, so AutomaticPaymentMethods is removed rather than left alongside.
            PaymentMethodTypes = new List<string> { "card" },
            ReceiptEmail = userEmail,
            Metadata = new Dictionary<string, string> { ["OrderId"] = order.Id.ToString(), ["UserId"] = userId }
        };

        try
        {
            var service = new Stripe.PaymentIntentService();
            var intent = await service.CreateAsync(piOptions);
            // Note: uses the dedicated StripePaymentIntentId column (not StripeSessionId) so the
            // existing payment_intent.succeeded webhook — which looks orders up by that column —
            // still works as the server-side fulfillment safety net under this PaymentIntent-only flow.
            order.StripePaymentIntentId = intent.Id;
            await _context.SaveChangesAsync();

            return Json(new { success = true, clientSecret = intent.ClientSecret, orderId = order.Id, publishableKey = _configuration["Stripe:PublishableKey"] });
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe failed to create PaymentIntent for order {OrderId}.", order.Id);
            return Json(new { success = false, message = ex.StripeError?.Message ?? ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmOrder([FromBody] ConfirmOrderRequest request)
    {
        var service = new Stripe.PaymentIntentService();
        Stripe.PaymentIntent intent;
        try
        {
            intent = await service.GetAsync(request.PaymentIntentId);
        }
        catch (Stripe.StripeException)
        {
            return Json(new { success = false, message = _localizer["We couldn't verify that payment with Stripe."].Value });
        }

        if (intent.Status != "succeeded")
            return Json(new { success = false, message = _localizer["Your payment could not be confirmed. Please try again."].Value });

        var userId = _userManager.GetUserId(User)!;
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.StripePaymentIntentId == intent.Id && o.UserId == userId);
        if (order == null)
            return Json(new { success = false, message = _localizer["We couldn't find that order."].Value });

        var result = await _fulfillment.FulfillPaidOrderAsync(order.Id, intent.Id);
        if (result.NewlyPaid && !string.IsNullOrEmpty(result.LoyaltyCouponCode))
        {
            TempData["NewLoyaltyCouponCode"] = result.LoyaltyCouponCode;
            TempData["NewLoyaltyDiscountPercent"] = result.LoyaltyDiscountPercent.ToString("0.##");
        }

        _cartService.ClearCart();
        HttpContext.Session.Remove(PendingShippingSessionKey);
        HttpContext.Session.Remove(AppliedCouponSessionKey);
        HttpContext.Session.Remove(OtpVerifiedSessionKey);

        return Json(new { success = true, redirectUrl = Url.Action(nameof(Confirmation), new { id = order.Id }) });
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

    public class OtpVerifyRequest { public string Code { get; set; } = string.Empty; }
    public class ConfirmOrderRequest { public string PaymentIntentId { get; set; } = string.Empty; }
}
