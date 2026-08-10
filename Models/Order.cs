using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Shipped = 2,
    Delivered = 3,
    // Added after Delivered (not inserted before it) so the existing Delivered=3 stays stable
    // for any orders already stored with that value — Status persists as a plain integer.
    OutForDelivery = 4
}

public class Order
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [Required, StringLength(150)]
    public string ShippingName { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string ShippingAddress { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string ShippingCity { get; set; } = string.Empty;

    [Required, StringLength(60)]
    public string ShippingState { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string ShippingZip { get; set; } = string.Empty;

    [Required, StringLength(30)]
    public string ShippingPhone { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal ShippingCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Total { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    [StringLength(100)]
    public string? StripeSessionId { get; set; }

    [StringLength(100)]
    public string? StripePaymentIntentId { get; set; }

    public bool IsPaid { get; set; }

    /// <summary>
    /// Coupon code applied at checkout, if any. Carried on the order so it can be
    /// marked "used" only after the payment is verified (in fulfillment), not before.
    /// </summary>
    [StringLength(40)]
    public string? CouponCode { get; set; }

    /// <summary>Manually entered by staff — no FedEx API account yet, so this is just the raw
    /// number used to build a link to FedEx's own public tracking page.</summary>
    [StringLength(40)]
    public string? TrackingNumber { get; set; }

    [StringLength(30)]
    public string? Carrier { get; set; } = "FedEx";

    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    [NotMapped]
    public string? TrackingUrl => string.IsNullOrEmpty(TrackingNumber)
        ? null
        : $"https://www.fedex.com/fedextrack/?trknbr={Uri.EscapeDataString(TrackingNumber)}";

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
