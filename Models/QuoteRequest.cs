using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorldLinkMaster.Web.Models;

public enum QuoteStatus
{
    Pending,
    Quoted,
    Approved,
    Rejected
}

public class QuoteRequest
{
    public int Id { get; set; }

    // Unguessable lookup key for the public confirmation page, since guest submissions have no
    // user account to check ownership against — prevents enumerating other people's requests
    // (which include email/phone) via the sequential Id.
    public Guid ConfirmationToken { get; set; } = Guid.NewGuid();

    [Required, StringLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(50)]
    public string? TradeLicenseNumber { get; set; }

    [Required, StringLength(2000)]
    public string ProductDetails { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public QuoteStatus Status { get; set; } = QuoteStatus.Pending;

    // Linked automatically when the submitter is signed in; null for guest submissions.
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? QuotedPrice { get; set; }

    [StringLength(1000)]
    public string? AdminNotes { get; set; }

    public DateTime? RespondedAt { get; set; }
}
