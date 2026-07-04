using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface ICouponService
{
    /// <summary>Creates a first-order welcome coupon for the user if they don't already have one. Returns the new coupon, or null if they already had one.</summary>
    Task<Coupon?> EnsureWelcomeCouponAsync(string userId);

    /// <summary>Call after an order is confirmed paid. Every 10th paid order earns a fresh 50%-off coupon. Returns the new coupon, or null if not eligible this time.</summary>
    Task<Coupon?> IssueLoyaltyCouponIfEligibleAsync(string userId);

    Task<Coupon?> ValidateAsync(string code, string userId);

    Task<List<Coupon>> GetUserCouponsAsync(string userId);

    Task MarkUsedAsync(Coupon coupon, int orderId);
}
