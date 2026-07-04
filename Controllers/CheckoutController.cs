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
    private const string AppliedCouponSessionKey = "AppliedCouponCode";
    private const string OtpCodeSessionKey = "CheckoutOtpCode";
    private const string OtpExpirySessionKey = "CheckoutOtpExpiry";
    private const string OtpAttemptsSessionKey = "CheckoutOtpAttempts";
    private const string OtpEmailSessionKey = "CheckoutOtpEmail";
    private const int OtpValidityMinutes = 10;
    private const int OtpMaxAttempts = 5;
    private const decimal PlatformCommissionRate = 0.10m;

    private readonly ICartService _cartService;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IStripeConnectService _stripeConnect;
    private readonly ICouponService _couponService;
    private readonly IEmailService _emailService;

    public CheckoutController(ICartService cartService, ApplicationDbContext context, UserManager<ApplicationUser> userManager, IConfiguration configuration, IStripeConnectService stripeConnect, ICouponService couponService, IEmailService emailService)
    {
        _cartService = cartService;
        _context = context;
        _userManager = userManager;
        _configuration = configuration;
        _stripeConnect = stripeConnect;
        _couponService = couponService;
        _emailService = emailService;
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
            TempData["CheckoutMessage"] = "That coupon code isn't valid, has already been used, or has expired.";
        }
        else
        {
            HttpContext.Session.SetString(AppliedCouponSessionKey, coupon.Code);
            TempData["CheckoutMessage"] = $"Coupon {coupon.Code} applied — {coupon.DiscountPercent:0.##}% off!";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveCoupon()
    {
        HttpContext.Session.Remove(AppliedCouponSessionKey);
        TempData["CheckoutMessage"] = "Coupon removed.";
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
            ModelState.AddModelError(string.Empty, "Stripe isn't configured yet. Add your Stripe test API keys to appsettings.json to enable checkout.");
            return View("Index", model);
        }

        // Stash the shipping form so it survives the redirect through OTP verification and out to Stripe.
        HttpContext.Session.SetString(PendingShippingSessionKey, JsonSerializer.Serialize(model));

        var user = await _userManager.GetUserAsync(User);
        var email = user?.Email ?? User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            ModelState.AddModelError(string.Empty, "We couldn't find an email address on your account to send a verification code to.");
            return View("Index", model);
        }

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
    public IActionResult VerifyOtp(string code)
    {
        var shippingJson = HttpContext.Session.GetString(PendingShippingSessionKey);
        var storedCode = HttpContext.Session.GetString(OtpCodeSessionKey);
        var storedExpiry = HttpContext.Session.GetString(OtpExpirySessionKey);

        if (string.IsNullOrEmpty(shippingJson) || string.IsNullOrEmpty(storedCode) || string.IsNullOrEmpty(storedExpiry))
        {
            TempData["CheckoutMessage"] = "Your checkout session expired. Please start again.";
            return RedirectToAction(nameof(Index));
        }

        var model = JsonSerializer.Deserialize<CheckoutViewModel>(shippingJson)!;
        var attempts = int.TryParse(HttpContext.Session.GetString(OtpAttemptsSessionKey), out var a) ? a : 0;

        if (attempts >= OtpMaxAttempts)
        {
            ModelState.AddModelError(string.Empty, "Too many incorrect attempts. Request a new code.");
            SetOtpViewBag();
            return View();
        }

        if (DateTime.UtcNow > DateTime.Parse(storedExpiry).ToUniversalTime())
        {
            ModelState.AddModelError(string.Empty, "That code has expired. Request a new one.");
            SetOtpViewBag();
            return View();
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim() != storedCode)
        {
            HttpContext.Session.SetString(OtpAttemptsSessionKey, (attempts + 1).ToString());
            ModelState.AddModelError(string.Empty, "That code isn't correct. Please try again.");
            SetOtpViewBag();
            return View();
        }

        HttpContext.Session.Remove(OtpCodeSessionKey);
        HttpContext.Session.Remove(OtpExpirySessionKey);
        HttpContext.Session.Remove(OtpAttemptsSessionKey);
        HttpContext.Session.Remove(OtpEmailSessionKey);

        return CreateStripeCheckoutSession(model);
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
        TempData["CheckoutMessage"] = "We sent you a new code.";
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
        // No SMTP configured yet — surface the code on-screen so the flow stays fully testable.
        ViewBag.DevOtpCode = _emailService.IsConfigured ? null : HttpContext.Session.GetString(OtpCodeSessionKey);
    }

    private IActionResult CreateStripeCheckoutSession(CheckoutViewModel model)
    {
        var cart = _cartService.GetCart();
        if (cart.Count == 0)
        {
            return RedirectToAction("Index", "Cart");
        }

        var discountedCart = ApplyCouponToCart(cart, model.CouponDiscountPercent);

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
            TempData["CheckoutMessage"] = $"Stripe couldn't start checkout: {ex.StripeError?.Message ?? ex.Message}";
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
            var existingUserId = _userManager.GetUserId(User);
            var existing = await _context.Orders
                .Where(o => o.UserId == existingUserId && o.StripeSessionId == session_id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                return RedirectToAction(nameof(Confirmation), new { id = existing.Id });
            }

            return RedirectToAction("Index", "Products");
        }

        var userId = _userManager.GetUserId(User)!;
        var discountedCart = ApplyCouponToCart(cart, shipping.CouponDiscountPercent);
        var orderItems = discountedCart.Select(i => new OrderItem
        {
            ProductId = i.ProductId,
            ProductName = i.Name,
            UnitPrice = i.UnitPrice,
            Quantity = i.Quantity,
            LineTotal = i.LineTotal,
            Color = i.Color,
            Size = i.Size,
            MerchantId = i.MerchantId
        }).ToList();
        var subtotal = orderItems.Sum(i => i.LineTotal);

        var order = new Order
        {
            UserId = userId,
            ShippingName = shipping.ShippingName,
            ShippingAddress = shipping.ShippingAddress,
            ShippingCity = shipping.ShippingCity,
            ShippingState = shipping.ShippingState,
            ShippingZip = shipping.ShippingZip,
            ShippingPhone = shipping.ShippingPhone,
            Subtotal = subtotal,
            ShippingCost = shipping.ShippingCost,
            Total = subtotal + shipping.ShippingCost,
            Status = OrderStatus.Pending,
            IsPaid = true,
            StripeSessionId = session.Id,
            StripePaymentIntentId = session.PaymentIntentId,
            Items = orderItems
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        if (!string.IsNullOrEmpty(shipping.AppliedCouponCode))
        {
            var usedCoupon = await _couponService.ValidateAsync(shipping.AppliedCouponCode, userId);
            if (usedCoupon != null)
            {
                await _couponService.MarkUsedAsync(usedCoupon, order.Id);
            }
        }

        var newLoyaltyCoupon = await _couponService.IssueLoyaltyCouponIfEligibleAsync(userId);
        if (newLoyaltyCoupon != null)
        {
            TempData["NewLoyaltyCouponCode"] = newLoyaltyCoupon.Code;
            TempData["NewLoyaltyDiscountPercent"] = newLoyaltyCoupon.DiscountPercent.ToString("0.##");
        }

        await CreateMerchantPayoutsAsync(order);

        var orderUser = await _userManager.GetUserAsync(User);
        var confirmationEmail = orderUser?.Email ?? User.Identity?.Name;
        if (!string.IsNullOrEmpty(confirmationEmail))
        {
            await _emailService.SendOrderConfirmationAsync(order, confirmationEmail);
        }

        _cartService.ClearCart();
        HttpContext.Session.Remove(PendingShippingSessionKey);
        HttpContext.Session.Remove(AppliedCouponSessionKey);

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
