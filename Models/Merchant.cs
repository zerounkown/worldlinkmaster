using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class Merchant
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(150)]
    public string BusinessName { get; set; } = string.Empty;

    [Required, StringLength(170)]
    public string Slug { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(100)]
    public string? StripeAccountId { get; set; }

    public bool StripeOnboardingComplete { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
