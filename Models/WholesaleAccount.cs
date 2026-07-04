using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class WholesaleAccount
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, StringLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [StringLength(60)]
    public string? TradeLicenseNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
