using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum CouponSource
{
    Welcome,
    LoyaltyMilestone,
    AdminPromo
}

public class Coupon
{
    public int Id { get; set; }

    [Required, StringLength(30)]
    [Display(Name = "Coupon Code")]
    public string Code { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Description { get; set; }

    // Personal coupons (Welcome/Loyalty) belong to a single user. Admin promo coupons are
    // public — UserId is null and any customer can redeem them (subject to the limits below).
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    [Range(0, 100)]
    [Display(Name = "Discount %")]
    public decimal DiscountPercent { get; set; }

    public CouponSource Source { get; set; }

    // --- Validity window ---

    [DataType(DataType.Date)]
    [Display(Name = "Start Date")]
    public DateTime? StartsAt { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Expiry Date")]
    public DateTime? ExpiresAt { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    // --- Public (AdminPromo) redemption limits ---

    /// <summary>Total number of times a public coupon may be redeemed across all customers. Null = unlimited.</summary>
    [Display(Name = "Max Redemptions")]
    public int? MaxRedemptions { get; set; }

    /// <summary>How many times a public coupon has been redeemed so far.</summary>
    public int RedemptionCount { get; set; }

    // --- Personal single-use tracking (Welcome/Loyalty) ---

    public bool IsUsed { get; set; }

    public DateTime? UsedAt { get; set; }

    public int? OrderId { get; set; }
    public Order? Order { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CouponRedemption> Redemptions { get; set; } = new List<CouponRedemption>();
}
