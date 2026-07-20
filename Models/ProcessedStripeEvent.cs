using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

/// <summary>
/// Ledger of Stripe webhook event IDs we have already handled, so retried or
/// duplicated deliveries are processed at most once (idempotency).
/// </summary>
public class ProcessedStripeEvent
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string EventId { get; set; } = string.Empty;

    [StringLength(60)]
    public string? EventType { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
