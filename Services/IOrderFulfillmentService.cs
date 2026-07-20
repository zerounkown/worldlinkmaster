using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Services;

/// <summary>Outcome of a fulfillment attempt.</summary>
/// <param name="NewlyPaid">True only for the single caller that transitioned the order to paid.</param>
public record FulfillmentResult(bool NewlyPaid, string? LoyaltyCouponCode = null, decimal LoyaltyDiscountPercent = 0);

public interface IOrderFulfillmentService
{
    /// <summary>
    /// Persists an unpaid, Pending order (a snapshot of the discounted cart) before the customer
    /// is redirected to Stripe, so payment can be reconciled server-side via webhook regardless of
    /// whether the browser ever returns to the success page.
    /// </summary>
    Task<Order> CreatePendingOrderAsync(string userId, CheckoutViewModel shipping, List<CartItem> discountedItems);

    /// <summary>
    /// Marks a previously-created order paid and runs post-payment side effects (merchant payouts,
    /// coupon redemption, loyalty reward, confirmation email) exactly once. Safe to call from both
    /// the Stripe webhook and the success page — a race-free conditional update guarantees the side
    /// effects run for only one caller.
    /// </summary>
    Task<FulfillmentResult> FulfillPaidOrderAsync(int orderId, string? paymentIntentId);
}
