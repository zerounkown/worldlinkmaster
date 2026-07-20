using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum PayoutStatus
{
    Pending,
    Transferred,
    Failed
}

public class MerchantPayout
{
    public int Id { get; set; }

    public int OrderId { get; set; }
    public Order? Order { get; set; }

    public int MerchantId { get; set; }
    public Merchant? Merchant { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal PlatformFee { get; set; }

    [StringLength(100)]
    public string? StripeTransferId { get; set; }

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    [StringLength(500)]
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
