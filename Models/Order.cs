using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum OrderStatus
{
    Pending,
    Processing,
    Shipped,
    Delivered
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

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}
