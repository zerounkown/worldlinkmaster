using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface ICouponService
{
    /// <summary>Creates a first-order welcome coupon for the user if they don't already have one. Returns the new coupon, or null if they already had one.</summary>
    Task<Coupon?> EnsureWelcomeCouponAsync(string userId);

    /// <summary>Call after an order is confirmed paid. Every 10th paid order earns a fresh 50%-off coupon. Returns the new coupon, or null if not eligible this time.</summary>
    Task<Coupon?> IssueLoyaltyCouponIfEligibleAsync(string userId);

    /// <summary>Validates a coupon code for a user. Handles both personal (single-use) coupons and public admin promo coupons (validity window, active flag, redemption limits, one-per-customer). Returns the coupon if it can be applied, otherwise null.</summary>
    Task<Coupon?> ValidateAsync(string code, string userId);

    /// <summary>Personal coupons issued to the user (Welcome / Loyalty).</summary>
    Task<List<Coupon>> GetUserCouponsAsync(string userId);

    /// <summary>Public promo coupons that are currently redeemable by the given user.</summary>
    Task<List<Coupon>> GetActivePublicCouponsAsync(string userId);

    /// <summary>Records that a coupon was used on an order. Personal coupons are marked single-use; public coupons record a per-customer redemption and increment the counter.</summary>
    Task MarkUsedAsync(Coupon coupon, int orderId, string userId);
}
