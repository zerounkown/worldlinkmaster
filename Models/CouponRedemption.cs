using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

/// <summary>
/// Records that a customer redeemed a public (AdminPromo) coupon on an order. Used to enforce
/// one-redemption-per-customer and to audit how a promo code was used.
/// </summary>
public class CouponRedemption
{
    public int Id { get; set; }

    public int CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public DateTime RedeemedAt { get; set; } = DateTime.UtcNow;
}
