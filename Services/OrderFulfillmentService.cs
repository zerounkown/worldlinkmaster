using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Services;

public class OrderFulfillmentService : IOrderFulfillmentService
{
    private const decimal PlatformCommissionRate = 0.10m;

    private readonly ApplicationDbContext _context;
    private readonly ICouponService _couponService;
    private readonly IEmailService _emailService;
    private readonly IStripeConnectService _stripeConnect;
    private readonly ILogger<OrderFulfillmentService> _logger;

    public OrderFulfillmentService(
        ApplicationDbContext context,
        ICouponService couponService,
        IEmailService emailService,
        IStripeConnectService stripeConnect,
        ILogger<OrderFulfillmentService> logger)
    {
        _context = context;
        _couponService = couponService;
        _emailService = emailService;
        _stripeConnect = stripeConnect;
        _logger = logger;
    }

    public async Task<Order> CreatePendingOrderAsync(string userId, CheckoutViewModel shipping, List<CartItem> discountedItems)
    {
        var orderItems = discountedItems.Select(i => new OrderItem
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
            IsPaid = false,
            CouponCode = shipping.AppliedCouponCode,
            Items = orderItems
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created pending order {OrderId} for user {UserId} totalling {Total} AED ({ItemCount} items).",
            order.Id, userId, order.Total, orderItems.Count);

        return order;
    }

    public async Task<FulfillmentResult> FulfillPaidOrderAsync(int orderId, string? paymentIntentId)
    {
        // Atomically claim the order: only the first caller to flip IsPaid from false->true wins,
        // so payouts/coupon/loyalty/email run exactly once even if the webhook and success page race.
        var claimed = await _context.Orders
            .Where(o => o.Id == orderId && !o.IsPaid)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.IsPaid, true)
                .SetProperty(o => o.StripePaymentIntentId, paymentIntentId));

        if (claimed == 0)
        {
            _logger.LogInformation("Order {OrderId} was already fulfilled (or does not exist); skipping duplicate processing.", orderId);
            return new FulfillmentResult(false);
        }

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            _logger.LogWarning("Order {OrderId} claimed for fulfillment but could not be reloaded.", orderId);
            return new FulfillmentResult(false);
        }

        _logger.LogInformation("Fulfilling paid order {OrderId} (PaymentIntent {PaymentIntentId}).", orderId, paymentIntentId);

        await CreateMerchantPayoutsAsync(order);

        if (!string.IsNullOrEmpty(order.CouponCode))
        {
            var usedCoupon = await _couponService.ValidateAsync(order.CouponCode, order.UserId);
            if (usedCoupon != null)
            {
                await _couponService.MarkUsedAsync(usedCoupon, order.Id, order.UserId);
                _logger.LogInformation("Coupon {CouponCode} redeemed on order {OrderId}.", order.CouponCode, order.Id);
            }
        }

        var loyaltyCoupon = await _couponService.IssueLoyaltyCouponIfEligibleAsync(order.UserId);
        if (loyaltyCoupon != null)
        {
            _logger.LogInformation("Issued loyalty coupon {CouponCode} to user {UserId} after order {OrderId}.",
                loyaltyCoupon.Code, order.UserId, order.Id);
        }

        var email = await _context.Users
            .Where(u => u.Id == order.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(email))
        {
            await _emailService.SendOrderConfirmationAsync(order, email);
        }

        return new FulfillmentResult(true, loyaltyCoupon?.Code, loyaltyCoupon?.DiscountPercent ?? 0);
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
                    _logger.LogInformation("Transferred {Amount} AED to merchant {MerchantId} for order {OrderId} (transfer {TransferId}).",
                        netAmount, merchant.Id, order.Id, payout.StripeTransferId);
                }
                catch (Stripe.StripeException ex)
                {
                    payout.Status = PayoutStatus.Failed;
                    payout.FailureReason = ex.StripeError?.Message ?? ex.Message;
                    _logger.LogError(ex, "Stripe transfer to merchant {MerchantId} for order {OrderId} failed.", merchant.Id, order.Id);
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
}
